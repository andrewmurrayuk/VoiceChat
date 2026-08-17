# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first (cached layer unless the csproj changes)
COPY VoiceChat.csproj ./
RUN dotnet restore

# Then copy the rest and publish
COPY . ./
RUN dotnet publish -c Release -o /app/publish --no-restore

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# Render injects PORT at runtime; 8080 is just the local default.
ENV PORT=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "VoiceChat.dll"]
