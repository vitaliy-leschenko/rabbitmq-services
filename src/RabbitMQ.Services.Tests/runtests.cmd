@echo off
dotnet test --configuration Release
reportgenerator -reports:../TestResults/coverage.cobertura.xml -targetdir:../TestResults/coveragereport -reporttypes:Html
start ../TestResults/coveragereport/index.html
