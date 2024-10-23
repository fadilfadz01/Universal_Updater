@echo off
setlocal

set ffuMountedPath=%1
set outputLog=%2

:: Using FOR to strip enclosing quotes
for %%i in (%ffuMountedPath%) do set unquotedFFUPath=%%~i
set bcdPath=%unquotedFFUPath%\EFIESP\EFI\Microsoft\BOOT\BCD

echo [COMMAND]: bcdedit /store "%bcdPath%" /set {globalsettings} bootflow 0x802 >> %outputLog%
bcdedit /store "%bcdPath%" /set {globalsettings} bootflow 0x802

:: Query back to check
bcdedit /store "%bcdPath%" /enum {globalsettings}

:: Run bcdedit and parse the output for 'bootflow'
for /f "tokens=1,* delims=" %%A in ('bcdedit /store "%bcdPath%" /enum {globalsettings} ^| findstr /i "bootflow"') do (
    echo %%A
)

endlocal