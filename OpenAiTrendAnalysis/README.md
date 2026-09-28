# OpenAI Trend Analysis Service

Internal-only Python service for the Report time-trend analysis feature.

## Local setup

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
$env:OPENAI_API_KEY = "..."
$env:AI_TREND_INTERNAL_TOKEN = "a-long-random-shared-secret"
uvicorn app.main:app --host 127.0.0.1 --port 8011
```

Set the same `AI_TREND_INTERNAL_TOKEN` in the ASP.NET Core User Secrets key
`AiTrendAnalysis:InternalToken`. Do not commit either secret. The service binds only to loopback
and accepts only the ASP.NET Core service-to-service request; it must not be proxied by Nginx.

`AiTrendAnalysis:Model` defaults to `gpt-6-luna` and can be changed through User Secrets or the
production environment without code changes.

## Tests

Run from this directory so Python can import the local `app` package:

```powershell
..\.venv\Scripts\python.exe -m unittest discover -s .\tests -v
```

The tests mock the OpenAI client and never make a network request or consume API credits.
