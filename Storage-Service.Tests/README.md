# Anwendungstest

Dieses Projekt enthält ausschließlich den gezielt aktivierten Anwendungstest
mit xUnit und Playwright. Die ursprünglichen sieben Unit-Tests wurden erfolgreich
nach `../tests/unit` verschoben und erweitert.

- [Unit-Tests und Coverage](../tests/README.md): `npm test` aus dem Repository-Root; keine laufenden Dienste nötig.
- [Anwendungstest und Voraussetzungen](EndToEnd/README.md): echte lokale Anwendung, Docker und Testzugänge.
- [Testquelle](EndToEnd/FrontendToDeliveryTests.cs): Frontend → Gateway → RabbitMQ → Storage → Supabase → delivery_queue.
- [Technische Hilfen](EndToEnd/Support/E2eSystem.cs): Infrastruktur prüfen und Nachweise sammeln.

Den Anwendungstest separat aus dem Repository-Root starten:

```powershell
.\Storage-Service.Tests\Run-E2E.ps1 -ComposeFile "C:\Pfad\zur\lokalen\compose.yaml" -UseLocalStackConfig
```

Der Helfer führt dieses Projekt aus: **ein Anwendungstest**, keine Unit-Tests.
Ohne `E2E_RUN=1` wird der Anwendungstest übersprungen.
Testnachrichten bleiben erhalten. Nachweise landen unter `TestResults`; Details
und Grenzen stehen in der [Anleitung](EndToEnd/README.md).
