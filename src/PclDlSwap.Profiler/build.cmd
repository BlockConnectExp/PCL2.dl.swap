@echo off
rem Builds the x86 CLR profiler. Requires VS Build Tools with the C++ workload.
setlocal

set "VCVARS="
if exist "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars32.bat" set "VCVARS=C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars32.bat"
if not defined VCVARS if exist "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat" set "VCVARS=C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat"
if not defined VCVARS if exist "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars32.bat" set "VCVARS=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars32.bat"

if not defined VCVARS (
  echo [build] vcvars32.bat not found
  exit /b 1
)

call "%VCVARS%" >nul
if errorlevel 1 (
  echo [build] vcvars32.bat failed
  exit /b 1
)

set "NETFXSDK=C:\Program Files (x86)\Windows Kits\NETFXSDK\4.7.2\Include\um"
pushd "%~dp0"
set "OUT=..\..\dist"
if not exist "%OUT%" mkdir "%OUT%"
if not exist obj mkdir obj

echo [build] compiling PclDlSwap.Profiler.dll
cl /nologo /LD /O2 /MT /W3 /EHsc /D_WIN32_WINNT=0x0601 /DUNICODE /D_UNICODE ^
  /I"%NETFXSDK%" ^
  Profiler.cpp ProfilerCallback.cpp DllMain.cpp ^
  /Fe:"%OUT%\PclDlSwap.Profiler.dll" ^
  /Fo:obj\ ^
  /link /DEF:PclDlSwap.Profiler.def /DLL

if errorlevel 1 (
  echo [build] FAILED
  popd
  exit /b 1
)

echo [build] OK
dir "%OUT%\PclDlSwap.Profiler.dll" | findstr /C:"PclDlSwap.Profiler.dll"
popd
endlocal

rem GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
