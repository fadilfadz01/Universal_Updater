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