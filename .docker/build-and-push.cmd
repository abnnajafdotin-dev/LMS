@echo off
setlocal
set "IMAGE=repo.dayainfra.ir/lms/stage:latest"

docker build --file "%~dp0Dockerfile" --tag "%IMAGE%" "%~dp0.."
if errorlevel 1 exit /b %errorlevel%

docker push "%IMAGE%"
exit /b %errorlevel%
