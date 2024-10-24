@echo off
setlocal
set ffuMountedPath=%1
set outputLog=%2

:: Using FOR to strip enclosing quotes
for %%i in (%ffuMountedPath%) do set unquotedFFUPath=%%~i

set softwarePath="%unquotedFFUPath%\Windows\System32\config\SOFTWARE"
set systemPath="%unquotedFFUPath%\Windows\System32\config\SYSTEM"

echo Unloading registry HKLM\TempSoftware >> %outputLog%
reg unload HKLM\TempSoftware >nul 2>&1

echo Unloading registry HKLM\TempSystem >> %outputLog%
reg unload HKLM\TempSystem >nul 2>&1

echo Loading registry %softwarePath% >> %outputLog%
reg load HKLM\TempSoftware %softwarePath% >nul 2>&1
if errorlevel 1 (
    echo Failed to load SOFTWARE hive. >> %outputLog%
)

echo Loading registry %systemPath% >> %outputLog%
reg load HKLM\TempSystem %systemPath% >nul 2>&1
if errorlevel 1 (
    echo Failed to load SYSTEM hive. >> %outputLog%
)

set systemInfo=HKLM\TempSoftware\Microsoft\Windows NT\CurrentVersion
set deviceTargetingInfo=HKLM\TempSystem\Platform\DeviceTargetingInfo
:: reg query "%deviceTargetingInfo%" >> %outputLog%

for /f "skip=2 tokens=1,2*" %%A in ('reg query "%deviceTargetingInfo%" ^| findstr /r "REG_SZ.*"') do (
    echo %%A: %%C
    echo.
)

for /f "skip=2 tokens=1,2*" %%A in ('reg query "%systemInfo%" ^| findstr /r "REG_SZ.*"') do (
    echo %%A: %%C
    echo.
)

echo Unloading registry HKLM\TempSoftware >> %outputLog%
reg unload HKLM\TempSoftware >nul 2>&1

echo Unloading registry HKLM\TempSystem >> %outputLog%
reg unload HKLM\TempSystem >nul 2>&1

echo Finished
endlocal
