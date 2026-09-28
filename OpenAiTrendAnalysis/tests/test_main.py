import json
import os
import unittest
from unittest.mock import Mock, patch

from fastapi import HTTPException
from fastapi.testclient import TestClient
from pydantic import ValidationError

from app.main import TrendAnalysisInput, analyze_time_trend, app, require_internal_token


def valid_payload() -> dict:
    return {
        "range": {
            "startDate": "2026-09-01",
            "endDateInclusive": "2026-09-30",
            "timeZoneId": "Asia/Taipei",
        },
        "dailyTotals": [
            {"date": "2026-09-01", "durationSeconds": 7200},
            {"date": "2026-09-02", "durationSeconds": 3600},
        ],
        "categoryTotals": [{"name": "工作", "durationSeconds": 10800}],
        "tagTotals": [{"name": "專注", "durationSeconds": 7200}],
        "derivedMetrics": {
            "totalSeconds": 10800,
            "trackedDayCount": 2,
            "calendarDayCount": 30,
            "averageSecondsPerTrackedDay": 5400,
            "firstHalfSeconds": 10800,
            "secondHalfSeconds": 0,
        },
    }


class TrendAnalysisServiceTests(unittest.TestCase):
    def test_payload_rejects_unknown_field(self) -> None:
        payload = valid_payload()
        payload["userId"] = 42

        with self.assertRaises(ValidationError):
            TrendAnalysisInput.model_validate(payload)

    def test_payload_rejects_duplicate_daily_dates(self) -> None:
        payload = valid_payload()
        payload["dailyTotals"].append({"date": "2026-09-01", "durationSeconds": 1})

        with self.assertRaises(ValidationError):
            TrendAnalysisInput.model_validate(payload)

    def test_internal_token_is_required(self) -> None:
        with patch.dict(os.environ, {"AI_TREND_INTERNAL_TOKEN": "expected"}, clear=False):
            with self.assertRaises(HTTPException) as error:
                require_internal_token("wrong")

        self.assertEqual(401, error.exception.status_code)

    def test_http_endpoint_rejects_a_request_without_the_internal_token(self) -> None:
        with patch.dict(os.environ, {"AI_TREND_INTERNAL_TOKEN": "expected"}, clear=False):
            response = TestClient(app).post("/analyze-time-trend", json=valid_payload())

        self.assertEqual(401, response.status_code)

    def test_missing_openai_key_returns_safe_503(self) -> None:
        payload = TrendAnalysisInput.model_validate(valid_payload())
        with patch.dict(os.environ, {"AI_TREND_INTERNAL_TOKEN": "internal"}, clear=True):
            with self.assertRaises(HTTPException) as error:
                analyze_time_trend(payload, None)

        self.assertEqual(503, error.exception.status_code)
        self.assertEqual("AI analysis is not configured.", error.exception.detail)

    @patch("app.main.OpenAI")
    def test_successful_structured_output_is_validated_and_returned(self, openai: Mock) -> None:
        response = Mock()
        response.status = "completed"
        response.output_text = json.dumps(
            {
                "dataSufficiency": "limited",
                "summary": "資料量有限，但已記錄 3 小時。",
                "observations": [{"claim": "9 月 1 日工時較高。", "evidence": "共 2 小時。"}],
                "suggestions": ["可持續記錄更多日期以觀察趨勢。"],
                "disclaimer": "僅依據所選期間的工時彙總。",
            }
        )
        openai.return_value.responses.create.return_value = response
        payload = TrendAnalysisInput.model_validate(valid_payload())

        with patch.dict(
            os.environ,
            {"OPENAI_API_KEY": "test-key", "AI_TREND_INTERNAL_TOKEN": "internal"},
            clear=True,
        ):
            result = analyze_time_trend(payload, None)

        self.assertEqual("limited", result.dataSufficiency.value)
        self.assertEqual("資料量有限，但已記錄 3 小時。", result.summary)
        self.assertEqual(1, len(result.observations))
        request = openai.return_value.responses.create.call_args.kwargs
        self.assertFalse(request["store"])
        self.assertEqual("json_schema", request["text"]["format"]["type"])
        self.assertEqual(3, request["text"]["format"]["schema"]["properties"]["observations"]["maxItems"])
        self.assertEqual(2, request["text"]["format"]["schema"]["properties"]["suggestions"]["maxItems"])


if __name__ == "__main__":
    unittest.main()
