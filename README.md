## Whisp's PRC API Worker

This project is a service that interacts with the PRC API to fetch and process data but also handles the caching, rate limiting, errors, scaling, etc. so that the main application can focus on its core functionality.

This project is built with the intention of being deployed on [railway.com](https://railway.com?referralCode=whisp), but will very easily work on any other platform.

## Quick Deploy

[Deploy to railway.](https://railway.com/deploy/qdR9Io?referralCode=whisp&utm_medium=integration&utm_source=template&utm_campaign=generic)
[Deploy to railway (without Redis).](https://railway.com/deploy/94yxat?referralCode=whisp&utm_medium=integration&utm_source=template&utm_campaign=generic)

## Documentation

### What is PRC?

PRC (Police Roleplay Community) is the owner of the Roblox game Emergency Response: Liberty County which provides and API for applications to communicate with in-game private servers. This program attempts to streamline this process since this API isn't known for being the most stable at times and it allows you to focus on the bulk of your application instead of writing all of the code that we have for you.

You can learn more about their API in their [docs](https://apidocs.policeroleplay.community).

### Environment Variables

| Varible Name          | Required |  Default   | Use                                                                                   |
| --------------------- | :------: | :--------: | ------------------------------------------------------------------------------------- |
| PRC_GLOBAL_KEY        |    ❌    |            | Your global API key from PRC, not required but may be needed for bigger applications. |
| AUTHORIZATION_KEY     |    ❌    |            | Required if `USE_AUTHORIZATION` is TRUE.                                              |
| USE_AUTHORIZATION     |    ❌    | Key Exists | Whether the `Authorization` header is required for PRC endpoints only.                |
| REDIS_HOST            |    ❌    |            | The host for your redis instance.                                                     |
| REDIS_PASSWORD        |    ❌    |            | The password for your redis instance.                                                 |
| REDIS_PORT            |    ❌    |            | The port for your redis instance.														|
| REDIS_PUBLISH_RESULTS |    ❌    |   FALSE    | Whether to publish results to redis channels for other services to consume.           |
| DISCORD_WEBHOOK_URL   |    ❌    |            | The webhook to send error alerts to.                                                  |

If you are using multiple replicas (to spread load), you should add a redis instance so that the replicas can work in sync, otherwise you will be recieving a ton of ratelimit errors.

Using the railway templates above, the `AUTHORIZATION_KEY` environment variable will be automatically generated upon the first deployment and the Redis based variables will be auto filled in the template that uses Redis, otherwise, you will be prompted to fill out those fields (optional) upon deploying the template without Redis.

⚠ Authentication and redis caching is disabled when in debug mode. ⚠

### Request Headers

| Header Name    |     Type      | Default | Use                                                                          |
| -------------- | :-----------: | :-----: | ---------------------------------------------------------------------------- |
| Authorization  |    String     |         | The authorization key matching the `AUTHORIZATION_KEY` environment variable. |
| Server-Key     |    String     |         | The plain text server API key for server related endpoints.                  |
| Use-Cache      |     Bool      |  TRUE   | Whether this request should use the cache.                                   |
| Cache-Duration | Int (Seconds) |   60    | How long the cache should last.                                              |
| Run-At         |   Timestamp   |  now()  | When this request should run.                                                |

**Security Note:** API keys are automatically hashed (SHA256) for caching, service communication and logging operations to prevent exposure.

Remember, while this service handles API key security internally, you should still protect your API keys in transit and storage within your own application.

### Publishing Results to Redis
If you have `REDIS_PUBLISH_RESULTS` enabled, unhashed API keys will be sent over the redis channels to other services to consume, so make sure that your redis instance is secure.

On a successful request, data will be sent to the `prcapiworker:update` channel in the following format: `{APIKEY}:{ENDPOINT}:{DATA}`, where the endpoint is a string representation of the `Endpoint` enum and the data is the raw JSON response from PRC.
On a failed request, error information will be sent to the `prcapiworker:failure` channel in the following format: `{APIKEY}:{ENDPOINT}:{ERRORCODE}:{ERRORMESSAGE}`, where the endpoint is a string represenation of the `Endpoint` enum.

## Support

Have any questions? You can contact our team in [our Discord](https://whisp.bot/support).
