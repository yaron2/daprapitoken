# Dapr to App Token Auth - C#

This sample shows a simple Dotnet service invocation between a Client and a Server using Dapr, with the Server authorizing requests from the Dapr sidecar using an API authentication.

## Running the sample locally

### Set a token for Dapr to send to the app

You can generate any token you want. For this example, we'll use an openssl generated token.
Set the token as an environment variable:

On Linux:

```bash
export APP_API_TOKEN=tZL3XdZQoLLWfLHn0UPWEg==
```

On Windows:

```bash
set APP_API_TOKEN=tZL3XdZQoLLWfLHn0UPWEg==
```

### Set the token for the app to authorize Dapr

Take the token you generated for Dapr and give it to your app. In your code, you will compare the token that Dapr sends you with the one your app has.

```bash
export MY_APP_TOKEN=tZL3XdZQoLLWfLHn0UPWEg==
```

On Windows:

```bash
set MY_APP_TOKEN=tZL3XdZQoLLWfLHn0UPWEg==
```

### Run the server

```bash
cd Server
dapr run --app-id routing --app-port 5000 -- dotnet run
```

### Run the client

```bash
cd Client
dapr run --app-id DaprClient -- dotnet run
```

### Observe the call went through

When Dapr invoked the Server app, it sent the token to be validated via an HTTP header.
The check is done in the following code:

```csharp
            async Task Deposit(HttpContext context)
            {
                // We try to get the token sent by the caller. If Dapr sent it, it will be populated
                context.Request.Headers.TryGetValue("dapr-api-token", out var token);

                // Get the token that we will authorize against from an environment variable. You'll want to put this in your program startup.
                // On Kubernetes, you will mount the token as an environment variable from a secret.
                var apiToken = System.Environment.GetEnvironmentVariable("MY_APP_TOKEN");

                // Validate the token we got from Dapr against the one we gave our app
                if (token != apiToken)
                {
                    logger.LogInformation("Unauthorized call rejected");
                    // Return unauthorized
                     context.Response.StatusCode = 401;
                    return;
                }

                logger.LogInformation("Enter Deposit");
```

### Try to invoke the app directly

Now you can try to call the app directly and see the call fail with 401 unauthorized.

```bash
curl -X POST http://127.0.0.1:5000/deposit -H 'Content-Type: application/json' -d '{"amount": 81.00}' -verbose
```

You can see the curl response returns with `401 unauthorized` and the logs of the Server app shows `== APP ==       Unauthorized call rejected`.

### Giving Dapr access to the token

When running on Kubernetes, there are no code changes in the application required. The only thing that changes is how we mount the token.First, create a Kubernetes secret containing the token:

*Note: the value of `token` needs to be a base64 encoded string*

```bash
kubectl create secret generic app-api-token --from-literal=token=tZL3XdZQoLLWfLHn0UPWEg==
```

Next, set the following annotation to configure Dapr to send the token from this secret on every request to your app:

```yaml
annotations:
  dapr.io/enabled: "true"
  dapr.io/app-id: "myapp"
  dapr.io/app-token-secret: "app-api-token" # name of the Kubernetes secret
```

You're done!

### Giving your app access to the token

You can mount the secret you created as an environment variable to your app pod:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: myapp
spec:
  selector:
    matchLabels:
      app: myapp
  replicas: 1
  template:
    metadata:
      labels:
        app: myapp
    spec:
      containers:
      - name: myapp
        image: myapp:1.15.0
        envFrom:
        - secretRef:
          name: app-api-token
        ports:
        - containerPort: 5000
```
### Additional Reference

https://docs.dapr.io/operations/security/app-api-token/
