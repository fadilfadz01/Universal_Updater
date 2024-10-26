:: **************************************************************************

::  Project Name:     Universal Updater
::  Description:      Console based unofficial updater for Windows Phone.

::  Author:           Fadil Fadz
::  Created Date:     2021

::  Contributors:     Bashar Astifan

::  Copyright © 2021 - 2024 Fadil Fadz

::  Permission is hereby granted, free of charge, to any person obtaining a copy 
::  of this software and associated documentation files (the "Software"), to deal 
::  in the Software without restriction, including without limitation the rights 
::  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
::  copies of the Software, and to permit persons to whom the Software is 
::  furnished to do so, subject to the following conditions:

::  The above copyright notice and this permission notice shall be included in all 
::  copies or substantial portions of the Software.

::  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
::  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
::  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
::  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
::  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
::  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE 
::  SOFTWARE.

:: **************************************************************************

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
