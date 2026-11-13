@echo off
rem NGII (national 1:5000 digital topographic map) -> Unity comparison overlay. Runs only on this PC.
rem Usage: double-click, or: run_ngii_overlay.bat D:\NGII_data
chcp 65001 >nul
setlocal
set "HERE=%~dp0"
set "DATA=%~1"
if "%DATA%"=="" set "DATA=D:\NGII_data"
set "OUT=%HERE%..\..\Assets\PNU_Map3\NGII"
set PYTHONUTF8=1
set PYTHONIOENCODING=utf-8
set "PY="
where py >nul 2>nul && set "PY=py -3"
if "%PY%"=="" ( where python >nul 2>nul && set "PY=python" )
if "%PY%"=="" (
  echo [!] Python 3 is not installed. Install Python 3.11 from https://www.python.org/downloads/ ^(check "Add to PATH"^) and run again.
  pause
  exit /b 1
)
if not exist "%DATA%" (
  echo [!] Data folder not found: %DATA%
  echo     Unzip the downloaded NGII SHP files there, or pass the folder: run_ngii_overlay.bat "D:\your\folder"
  pause
  exit /b 1
)
if not exist "%HERE%.venv\Scripts\python.exe" (
  echo Creating Python environment ^(first run only^)...
  %PY% -m venv "%HERE%.venv" || ( echo [!] venv failed & pause & exit /b 1 )
)
"%HERE%.venv\Scripts\python.exe" -m pip install --quiet --disable-pip-version-check pyshp pyproj shapely numpy mapbox-earcut || ( echo [!] pip install failed & pause & exit /b 1 )
"%HERE%.venv\Scripts\python.exe" "%HERE%ngii_overlay.py" --data "%DATA%" --ref "%HERE%ngii_ref.json" --out "%OUT%" || ( echo [!] conversion failed & pause & exit /b 1 )
rem Unity menu script (copied only when you run this tool)
copy /Y "%HERE%PNUNgiiOverlay.cs" "%HERE%..\..\Assets\PNU_Map3\Editor\PNUNgiiOverlay.cs" >nul
echo.
echo Done. In Unity: menu  PNU Map ^> NGII ...  (load overlay)
pause
