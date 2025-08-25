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

| Varible Name        | Required | Default | Use                                                                                   |
| ------------------- | :------: | :-----: | ------------------------------------------------------------------------------------- |
| PRC_GLOBAL_KEY      |    ❌    |         | Your global API key from PRC, not required but may be needed for bigger applications. |
| AUTHORIZATION_KEY   |    ❌    |         | Required if `USE_AUTHORIZATION` is TRUE.                                              |
| USE_AUTHORIZATION   |    ❌    |  FALSE  | Whether the `Authorization` header is required for PRC endpoints only.                |
| ENCRYPTION_KEY      |    ✅    |         | The encryption key to use for API key encryption. Should be 32 characters long.       |
| REDIS_HOST          |    ❌    |         | The host for your redis instance.                                                     |
| REDIS_PASSWORD      |    ❌    |         | The password for your redis instance.                                                 |
| REDIS_PORT          |    ❌    |         | The port for your redis instance.                                                     |
| DISCORD_WEBHOOK_URL |    ❌    |         | The webhook to send error alerts to.                                                  |

Using the railway templates above, the `AUTHORIZATION_KEY` and `ENCRYPTION_KEY` environment variables will be automatically generated upon the first deployment and the Redis based variables will be auto filled in the template that uses Redis, otherwise, you will be prompted to fill out those fields (optional) upon deploying the template without Redis.

⚠ Authentication and redis caching is disabled when in debug mode. ⚠

### Request Headers

| Header Name    |     Type      | Default | Use                                                                          |
| -------------- | :-----------: | :-----: | ---------------------------------------------------------------------------- |
| Authorization  |    String     |         | The authorization key matching the `AUTHORIZATION_KEY` environment variable. |
| Server-Key     |    String     |         | The encrypted server key, used for all server related endpoints.             |
| Use-Cache      |     Bool      |  TRUE   | Whether this request should use the cache.                                   |
| Cache-Duration | Int (Seconds) |   60    | How long the cache should last.                                              |
| Run-At         |   Timestamp   |  now()  | When this request should run.                                                |

Unsure how to encrypt your server key? We use AES encryption with the `ENCRYPTION_KEY` environment variable being the encryption key. Unsure how to implement this into your application?:

- We have code which you can use in /src/Encryption.cs if your application uses C#

```cs
public string EncryptApiKey(string apiKey)
{
    using var aes = Aes.Create();
    aes.Key = Encoding.UTF8.GetBytes(EncryptionKey);
    aes.GenerateIV();
    var iv = aes.IV;

    using var encryptor = aes.CreateEncryptor(aes.Key, iv);
    using var ms = new MemoryStream();
    ms.Write(iv, 0, iv.Length);
    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
    using (var sw = new StreamWriter(cs))
    {
        sw.Write(apiKey);
    }
    return Convert.ToBase64String(ms.ToArray());
}
```

- You can use the POST /encrypt-key endpoint with a body formatted as:

```json
{ "key": "SERVER_KEY_HERE" }
```

Remember, never store API keys in plain text.

## Support

Have any questions? You can contact our team in [our Discord](https://whisp.bot/support).
