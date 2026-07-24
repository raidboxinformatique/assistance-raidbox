@echo off
setlocal EnableExtensions

if not "%~1"=="" goto CopyFiles
goto Update

:CopyFiles
if "%~1"=="" exit /b 0
XCOPY "%~1" "%~dp0\" /Y /B /J /R /H /G /C /V /E /F
SHIFT
goto CopyFiles

:Update
set "ROOT=%~dp0"
set "GIT=%ROOT%git\mingw32\bin\git.exe"
set "RELAY=%ROOT%TeamViewerQS.exe"
set "RELAY_TEMP=%TEMP%\Raidbox-TeamViewerQS-1.37.exe"
set "RELAY_URL=https://github.com/raidboxinformatique/assistance-raidbox/releases/download/v1.37/TeamViewerQS.Legacy.exe"
set "RELAY_SHA256=0c73de3688187018fc3ad3f5e2d7a73de135dcd2c648b9d98c3276618b629269"
set "LOG_DIR=%LOCALAPPDATA%\Raidbox\Assistance\Logs"
set "LOG_FILE=%LOG_DIR%\legacy-update.log"
set "RELAY_READY=0"

if not exist "%LOG_DIR%" mkdir "%LOG_DIR%" >NUL 2>&1
call :Log "Demarrage de la recherche des mises a jour historiques."
echo Verification des mises a jour.

"%SystemRoot%\System32\taskkill.exe" /F /IM teamviewer.exe >NUL 2>&1
"%SystemRoot%\System32\taskkill.exe" /F /IM TeamViewerQS.exe >NUL 2>&1

if exist "%GIT%" (
    "%GIT%" -C "%ROOT%" fetch --quiet origin main >>"%LOG_FILE%" 2>&1
    if not errorlevel 1 (
        "%GIT%" -C "%ROOT%" checkout --force FETCH_HEAD -- TeamViewerQS.exe >>"%LOG_FILE%" 2>&1
        if not errorlevel 1 (
            "%RELAY%" --self-test >>"%LOG_FILE%" 2>&1
            if not errorlevel 1 set "RELAY_READY=1"
        )
    )
)

if "%RELAY_READY%"=="1" goto LaunchRelay

call :Log "Mise a jour Git impossible. Tentative de telechargement direct."
echo Telechargement direct de la mise a jour.
call :DownloadRelay
if errorlevel 1 goto UpdateFailed
set "RELAY_READY=1"

:LaunchRelay
call :Log "Relais de migration valide. Lancement."
echo Chargement en cours, veuillez patienter.
START "" "%RELAY%"
exit /b 0

:DownloadRelay
if not exist "%SystemRoot%\System32\curl.exe" (
    call :Log "curl.exe est introuvable."
    exit /b 1
)

if exist "%RELAY_TEMP%" del /F /Q "%RELAY_TEMP%" >NUL 2>&1
"%SystemRoot%\System32\curl.exe" --fail --location --silent --show-error --retry 2 --connect-timeout 15 --output "%RELAY_TEMP%" "%RELAY_URL%" >>"%LOG_FILE%" 2>&1
if errorlevel 1 (
    call :Log "Echec du telechargement direct."
    exit /b 1
)

call :VerifyDownloadedRelay
if errorlevel 1 exit /b 1

if exist "%RELAY%" ATTRIB -R -H -S "%RELAY%" >NUL 2>&1
COPY /Y "%RELAY_TEMP%" "%RELAY%" >NUL 2>&1
if errorlevel 1 (
    call :Log "Impossible de remplacer TeamViewerQS.exe."
    exit /b 1
)

del /F /Q "%RELAY_TEMP%" >NUL 2>&1
"%RELAY%" --self-test >>"%LOG_FILE%" 2>&1
if errorlevel 1 (
    call :Log "Le relais telecharge a echoue a son auto-test."
    exit /b 1
)
exit /b 0

:VerifyDownloadedRelay
set "HASH_FILE=%TEMP%\Raidbox-TeamViewerQS-Hash-%RANDOM%.txt"
"%SystemRoot%\System32\certutil.exe" -hashfile "%RELAY_TEMP%" SHA256 >"%HASH_FILE%" 2>&1
if errorlevel 1 (
    call :Log "Le calcul SHA-256 du relais a echoue."
    if exist "%HASH_FILE%" del /F /Q "%HASH_FILE%" >NUL 2>&1
    exit /b 1
)

"%SystemRoot%\System32\findstr.exe" /I /C:"%RELAY_SHA256%" "%HASH_FILE%" >NUL 2>&1
if errorlevel 1 (
    call :Log "Le controle SHA-256 du relais a echoue."
    del /F /Q "%HASH_FILE%" >NUL 2>&1
    del /F /Q "%RELAY_TEMP%" >NUL 2>&1
    exit /b 1
)

del /F /Q "%HASH_FILE%" >NUL 2>&1
exit /b 0

:UpdateFailed
call :Log "Echec de la mise a jour automatique historique."
echo La mise a jour automatique a echoue.
echo Le journal se trouve dans %LOG_FILE%
if exist "%RELAY%" (
    echo Ouverture de l'assistance existante.
    START "" "%RELAY%"
)
exit /b 1

:Log
>>"%LOG_FILE%" echo [%date% %time%] %~1
exit /b 0
