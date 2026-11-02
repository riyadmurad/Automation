FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY api/M365.SecurityAssessment.Domain/M365.SecurityAssessment.Domain.csproj ./api/M365.SecurityAssessment.Domain/
COPY api/M365.SecurityAssessment.Api/M365.SecurityAssessment.Api.csproj api/M365.SecurityAssessment.Api/packages.lock.json ./api/M365.SecurityAssessment.Api/
RUN dotnet restore api/M365.SecurityAssessment.Api/M365.SecurityAssessment.Api.csproj --locked-mode
COPY api/M365.SecurityAssessment.Domain/ ./api/M365.SecurityAssessment.Domain/
COPY api/M365.SecurityAssessment.Api/ ./api/M365.SecurityAssessment.Api/
COPY baselines/ ./baselines/
RUN dotnet publish api/M365.SecurityAssessment.Api/M365.SecurityAssessment.Api.csproj --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish ./
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "M365.SecurityAssessment.Api.dll"]