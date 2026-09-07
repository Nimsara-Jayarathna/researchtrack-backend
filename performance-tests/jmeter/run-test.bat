@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
if "%JMETER_BIN%"=="" set "JMETER_BIN=jmeter"
if "%PROTOCOL%"=="" set "PROTOCOL=http"
if "%HOST%"=="" set "HOST=localhost"
if "%PORT%"=="" set "PORT=5000"
if "%THREADS%"=="" set "THREADS=1"
if "%RAMPUP%"=="" set "RAMPUP=1"
if "%DURATION%"=="" set "DURATION=60"
if "%LOOPS%"=="" set "LOOPS=1"
if "%USERS_FILE%"=="" set "USERS_FILE=%SCRIPT_DIR%data\users.csv"
if "%THINK_TIME_MS%"=="" set "THINK_TIME_MS=500"
if "%THINK_TIME_JITTER_MS%"=="" set "THINK_TIME_JITTER_MS=500"

if not exist "%USERS_FILE%" (
  echo User CSV not found: %USERS_FILE%
  echo Copy data\users.csv.example to data\users.csv and add dedicated test credentials.
  exit /b 2
)

powershell -NoProfile -Command "$response = Invoke-WebRequest -UseBasicParsing -Uri '%PROTOCOL%://%HOST%:%PORT%/health/live' -TimeoutSec 5; if ($response.StatusCode -ne 200) { exit 1 }" >nul 2>&1
if errorlevel 1 (
  echo Target health check failed: %PROTOCOL%://%HOST%:%PORT%/health/live
  echo Start the ResearchTrack gateway and confirm that endpoint returns HTTP 200 before load testing.
  exit /b 3
)

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss"') do set "TIMESTAMP=%%i"
set "RESULTS_DIR=%SCRIPT_DIR%results"
set "REPORTS_DIR=%SCRIPT_DIR%reports"
set "RESULT_FILE=%RESULTS_DIR%\researchtrack-%TIMESTAMP%.jtl"
set "REPORT_DIR=%REPORTS_DIR%\researchtrack-%TIMESTAMP%"
if not exist "%RESULTS_DIR%" mkdir "%RESULTS_DIR%"
if not exist "%REPORTS_DIR%" mkdir "%REPORTS_DIR%"

echo Target: %PROTOCOL%://%HOST%:%PORT% ^| users: %THREADS% ^| ramp-up: %RAMPUP%s ^| duration cap: %DURATION%s ^| loops: %LOOPS%
"%JMETER_BIN%" -n -t "%SCRIPT_DIR%test-plan.jmx" -l "%RESULT_FILE%" -e -o "%REPORT_DIR%" -Jprotocol="%PROTOCOL%" -Jhost="%HOST%" -Jport="%PORT%" -Jthreads="%THREADS%" -Jrampup="%RAMPUP%" -Jduration="%DURATION%" -Jloops="%LOOPS%" -Jusers_file="%USERS_FILE%" -Jthink_time_ms="%THINK_TIME_MS%" -Jthink_time_jitter_ms="%THINK_TIME_JITTER_MS%"
