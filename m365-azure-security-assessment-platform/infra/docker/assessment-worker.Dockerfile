FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY api/M365.SecurityAssessment.Domain/M365.SecurityAssessment.Domain.csproj ./api/M365.SecurityAssessment.Domain/
COPY workers/M365.SecurityAssessment.AssessmentWorker/M365.SecurityAssessment.AssessmentWorker.csproj ./workers/M365.SecurityAssessment.AssessmentWorker/
RUN dotnet restore workers/M365.SecurityAssessment.AssessmentWorker/M365.SecurityAssessment.AssessmentWorker.csproj
COPY api/M365.SecurityAssessment.Domain/ ./api/M365.SecurityAssessment.Domain/
COPY workers/M365.SecurityAssessment.AssessmentWorker/ ./workers/M365.SecurityAssessment.AssessmentWorker/
RUN dotnet publish workers/M365.SecurityAssessment.AssessmentWorker/M365.SecurityAssessment.AssessmentWorker.csproj --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
USER app
ENTRYPOINT ["dotnet", "M365.SecurityAssessment.AssessmentWorker.dll"]