import json  # noqa: I001
import logging
import os
from enum import Enum
from typing import Annotated

from fastapi import Depends, FastAPI, Header, HTTPException, status
from openai import APIConnectionError, APIStatusError, APITimeoutError, OpenAI
from pydantic import BaseModel, ConfigDict, Field, ValidationError, field_validator


MAX_DAYS = 365
MAX_TOTALS = 10
logger = logging.getLogger(__name__)
SYSTEM_INSTRUCTIONS = """You analyze a user's time-tracking aggregates.
Return Traditional Chinese only. Use only facts contained in the supplied JSON.
Do not infer motives, causality, work quality, personality, health, or events not present.
Do not recalculate or contradict the supplied metrics. Every observation must include numeric
evidence. If data is sparse, use limited or insufficient and avoid strong conclusions.
Suggestions are optional, neutral, and actionable; never command the user. This is not a
productivity score, health assessment, or task plan."""
OPENAI_RESPONSE_SCHEMA = {
    "type": "object",
    "properties": {
        "dataSufficiency": {"type": "string", "enum": ["sufficient", "limited", "insufficient"]},
        "summary": {"type": "string"},
        "observations": {
            "type": "array",
            "maxItems": 3,
            "items": {
                "type": "object",
                "properties": {"claim": {"type": "string"}, "evidence": {"type": "string"}},
                "required": ["claim", "evidence"],
                "additionalProperties": False,
            },
        },
        "suggestions": {"type": "array", "maxItems": 2, "items": {"type": "string"}},
        "disclaimer": {"type": "string"},
    },
    "required": ["dataSufficiency", "summary", "observations", "suggestions", "disclaimer"],
    "additionalProperties": False,
}


class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid")


class Range(StrictModel):
    startDate: str
    endDateInclusive: str
    timeZoneId: str


class DailyTotal(StrictModel):
    date: str
    durationSeconds: int = Field(ge=0)


class NamedTotal(StrictModel):
    name: str = Field(min_length=1, max_length=120)
    durationSeconds: int = Field(ge=0)


class DerivedMetrics(StrictModel):
    totalSeconds: int = Field(ge=0)
    trackedDayCount: int = Field(ge=0)
    calendarDayCount: int = Field(ge=1, le=MAX_DAYS)
    averageSecondsPerTrackedDay: int = Field(ge=0)
    firstHalfSeconds: int = Field(ge=0)
    secondHalfSeconds: int = Field(ge=0)


class TrendAnalysisInput(StrictModel):
    range: Range
    dailyTotals: list[DailyTotal] = Field(max_length=MAX_DAYS)
    categoryTotals: list[NamedTotal] = Field(max_length=MAX_TOTALS)
    tagTotals: list[NamedTotal] = Field(max_length=MAX_TOTALS)
    derivedMetrics: DerivedMetrics

    @field_validator("dailyTotals")
    @classmethod
    def dates_must_be_unique(cls, values: list[DailyTotal]) -> list[DailyTotal]:
        if len({item.date for item in values}) != len(values):
            raise ValueError("dailyTotals dates must be unique")
        return values


class DataSufficiency(str, Enum):
    sufficient = "sufficient"
    limited = "limited"
    insufficient = "insufficient"


class Observation(StrictModel):
    claim: str
    evidence: str


class TrendAnalysis(StrictModel):
    dataSufficiency: DataSufficiency
    summary: str
    observations: list[Observation] = Field(max_length=3)
    suggestions: list[str] = Field(max_length=2)
    disclaimer: str


app = FastAPI(title="LTM OpenAI Trend Analysis", docs_url=None, redoc_url=None, openapi_url=None)


def require_internal_token(token: Annotated[str | None, Header(alias="X-AI-Internal-Token")] = None) -> None:
    expected = os.environ.get("AI_TREND_INTERNAL_TOKEN")
    if not expected or token != expected:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Unauthorized internal caller.")


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}


@app.post("/analyze-time-trend", response_model=TrendAnalysis)
def analyze_time_trend(payload: TrendAnalysisInput, _: None = Depends(require_internal_token)) -> TrendAnalysis:
    api_key = os.environ.get("OPENAI_API_KEY")
    if not api_key:
        raise HTTPException(status_code=status.HTTP_503_SERVICE_UNAVAILABLE, detail="AI analysis is not configured.")

    model = os.environ.get("OPENAI_MODEL", "gpt-6-luna")
    try:
        response = OpenAI(api_key=api_key, timeout=15.0, max_retries=0).responses.create(
            model=model,
            store=False,
            instructions=SYSTEM_INSTRUCTIONS,
            input=json.dumps(payload.model_dump(mode="json"), ensure_ascii=False),
            text={
                "format": {
                    "type": "json_schema",
                    "name": "time_trend_analysis",
                    "strict": True,
                    "schema": OPENAI_RESPONSE_SCHEMA,
                }
            },
        )
        if response.status != "completed" or not response.output_text:
            logger.warning("OpenAI trend analysis did not complete. status=%s", response.status)
            raise HTTPException(status_code=status.HTTP_502_BAD_GATEWAY, detail="AI analysis did not complete.")
        return TrendAnalysis.model_validate_json(response.output_text)
    except APITimeoutError as exc:
        raise HTTPException(status_code=status.HTTP_504_GATEWAY_TIMEOUT, detail="AI analysis timed out.") from exc
    except (APIConnectionError, APIStatusError) as exc:
        raise HTTPException(status_code=status.HTTP_503_SERVICE_UNAVAILABLE, detail="AI analysis is unavailable.") from exc
    except ValidationError as exc:
        # Log only error categories, never the model response or the user's aggregate data.
        error_types = [error["type"] for error in exc.errors()]
        logger.warning("OpenAI trend analysis failed response validation. error_types=%s", error_types)
        raise HTTPException(status_code=status.HTTP_502_BAD_GATEWAY, detail="AI analysis returned an invalid response.") from exc
