@echo off
set iutoolPath=%1
set pathToPackages=%2
set outputLog=%3

echo [COMMAND]: %iutoolPath% -V -p %pathToPackages% >> %outputLog%
%iutoolPath% -V -p %pathToPackages%

::Pause so user can see output before window close
pause