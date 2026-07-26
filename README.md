# Test_Task — платёжный сервис

Сервис создаёт платёжные операции, надёжно планирует отправку внешнему провайдеру и завершает операцию только по callback-квитанции.

## Запуск через Docker Compose

Требуется Docker Desktop с Compose:

```bash
docker compose up --build
```

После запуска:

- API: `http://localhost:8080`
- Swagger: `http://localhost:8080/swagger`
- provider-simulator: `http://localhost:8081`
- MSSQL: `localhost:1433`, база `Test_Task`, пользователь `sa`

MSSQL хранится в volume `mssql-data`. Миграции применяются автоматически при старте `candidate-service`.

## Сквозной сценарий

Создать операцию:

```bash
curl -i -X POST http://localhost:8080/operations \
  -H "Content-Type: application/json" \
  -d '{"operationId":"operation-123","amount":"1000.00","currency":"RUB","description":"Оплата заказа"}'
```

Запланировать отправку:

```bash
curl -i -X POST http://localhost:8080/operations/operation-123/submit
```

Проверить операцию и историю:

```bash
curl http://localhost:8080/operations/operation-123
curl http://localhost:8080/operations/operation-123/events
```

Провайдер сам отправит callback на `/receipts`. Для ручной проверки callback:

```bash
curl -i -X POST http://localhost:8080/receipts \
  -H "Content-Type: application/json" \
  -d '{"providerPaymentId":"provider-123","operationId":"operation-123","result":"COMPLETED","message":"Payment completed","occurredAt":"2026-07-26T12:00:00Z"}'
```

## API

- `GET /health`
- `POST /operations`
- `GET /operations/{id}`
- `GET /operations/{id}/events`
- `POST /operations/{id}/submit`
- `POST /receipts`

Повторные submit используют уже сохранённый dispatch job. Повторные HTTP-вызовы провайдера используют одинаковые `Idempotency-Key`, `X-Correlation-ID` и тело запроса. Финальный статус устанавливается только callback-квитанцией.

## Локальный запуск

Для запуска из Visual Studio или CLI нужен локальный MSSQL и connection string из `appsettings.Development.json`:

```bash
dotnet run --project Test_Task/Test_Task.csproj
```

Тесты запускаются так:

```bash
dotnet test Test_Task.Tests/Test_Task.Tests.csproj
```
