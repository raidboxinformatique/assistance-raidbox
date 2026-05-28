@echo off

IF "%1"=="" GOTO TeamViewer

:Loop
IF "%1"=="" GOTO Continue
	XCOPY "%1" "%~dp0\" /Y/B/J/R/H/G/C/V/E /F
	pause
SHIFT
GOTO Loop
:Continue
EXIT

:TeamViewer
ECHO Verification des mises a jour.

start /B /wait %~dp0\git\mingw32\bin\git.exe pull
ECHO Chargement en cours, veuillez patienter.

START /B /WAIT %SystemRoot%\System32\taskkill.exe /F /im teamviewer.exe > NUL 2>&1

IF NOT EXIST "%~dp0TeamViewerQS.exe" (
	ECHO Erreur : TeamViewerQS.exe est introuvable.
	pause
	EXIT /B 1
)

START "" "%~dp0TeamViewerQS.exe"
