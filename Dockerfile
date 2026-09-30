ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:10.0
FROM ${SDK_IMAGE} AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/ShoutCalendar.Sync.Relay/ShoutCalendar.Sync.Relay.csproj -c Release -p:RestoreLockedMode=true -o /out

FROM ${RUNTIME_IMAGE}
WORKDIR /app
COPY --from=build /out .
RUN mkdir /data && chown app:app /data
USER app
VOLUME /data
EXPOSE 8787
ENTRYPOINT ["dotnet", "ShoutCalendar.Sync.Relay.dll"]
CMD ["serve", "--store", "/data", "--http", "8787"]
