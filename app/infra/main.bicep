// Expovice — Azure infrastructure (resource-group scope).
// Everything is sized for "launch on free tiers": App Service F1, Azure SQL free offer (serverless, auto-pause),
// LRS storage, capped Log Analytics ingestion and an optional monthly budget alert.
// Deploy: az deployment group create -g <rg> -f infra/main.bicep -p infra/main.bicepparam

targetScope = 'resourceGroup'

@description('Azure region for everything except Communication Services (which is global).')
param location string = resourceGroup().location

@description('Short lowercase prefix used in resource names.')
@minLength(3)
@maxLength(10)
param namePrefix string = 'expovice'

@description('App Service plan SKU. F1 is free (sleeps when idle, 60 CPU-min/day). Move to B1 when you have real users.')
@allowed(['F1', 'B1', 'P0v3'])
param appServiceSku string = 'F1'

@description('Base64 HMAC key (>= 32 bytes) used to sign access tokens. Generate with: openssl rand -base64 48')
@secure()
param jwtSigningKey string

@description('Sender phone number bought in Azure Communication Services (E.164). Leave empty to keep SMS disabled.')
param smsFromNumber string = ''

@description('Google OAuth client id (web application type). Leave empty to disable Google sign-in.')
param googleClientId string = ''

@secure()
param googleClientSecret string = ''

@description('Apple Services ID used for Sign in with Apple. Leave empty to disable Apple sign-in.')
param appleClientId string = ''
param appleTeamId string = ''
param appleKeyId string = ''

@description('Contents of the Apple .p8 private key.')
@secure()
param applePrivateKey string = ''

@description('Monthly budget in the subscription currency. 0 disables the budget.')
param monthlyBudget int = 500

@description('Emails notified when 80% / 100% of the budget is reached. Empty disables the budget.')
param budgetContactEmails array = []

@description('First day of the month the budget starts (yyyy-MM-01).')
param budgetStartDate string = utcNow('yyyy-MM-01')

var suffix = uniqueString(resourceGroup().id)
var webAppName = '${namePrefix}-api-${suffix}'
var sqlServerName = '${namePrefix}-sql-${suffix}'
var sqlDatabaseName = 'expovice'
// Built as a string (not a resource reference) so the web app and the SQL server don't depend on each other.
var sqlFqdn = '${sqlServerName}${environment().suffixes.sqlServerHostname}'
var keyVaultName = take('${namePrefix}-kv-${suffix}', 24)
var storageName = take(toLower(replace('${namePrefix}st${suffix}', '-', '')), 24)
var smsEnabled = !empty(smsFromNumber)
var googleEnabled = !empty(googleClientId) && !empty(googleClientSecret)
var appleEnabled = !empty(appleClientId) && !empty(appleTeamId) && !empty(appleKeyId) && !empty(applePrivateKey)

// Built-in role definition ids.
var roleKeyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'
var roleStorageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

// ---------- Monitoring ----------

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-logs-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: 1 } // hard cap so a log storm can't run up the bill
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-ai-${suffix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// ---------- Storage (photos + ID documents) ----------

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  #disable-next-line BCP334 // always >= 15 chars: prefix (>= 3) + 'st' + 13-char uniqueString
  name: storageName
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false // photos are served through short-lived SAS URLs only
    allowSharedKeyAccess: false // forces Entra ID (managed identity) auth in Azure
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource photosContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'photos'
  properties: { publicAccess: 'None' }
}

resource idDocsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'id-documents'
  properties: { publicAccess: 'None' }
}

// ---------- SMS ----------

resource communication 'Microsoft.Communication/communicationServices@2023-04-01' = if (smsEnabled) {
  name: '${namePrefix}-acs-${suffix}'
  location: 'global'
  properties: { dataLocation: 'India' }
}

// ---------- Key Vault (secrets referenced by App Service settings) ----------

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

resource jwtSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'JwtSigningKey'
  properties: { value: jwtSigningKey }
}

resource smsSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (smsEnabled) {
  parent: keyVault
  name: 'SmsConnectionString'
  properties: { value: smsEnabled ? communication!.listKeys().primaryConnectionString : '' }
}

resource googleSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (googleEnabled) {
  parent: keyVault
  name: 'GoogleClientSecret'
  properties: { value: googleClientSecret }
}

resource appleSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (appleEnabled) {
  parent: keyVault
  name: 'ApplePrivateKey'
  properties: { value: applePrivateKey }
}

// ---------- API (App Service, Linux, .NET 10) ----------

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${namePrefix}-plan-${suffix}'
  location: location
  kind: 'linux'
  sku: { name: appServiceSku }
  properties: { reserved: true }
}

var kvRef = 'https://${keyVaultName}${environment().suffixes.keyvaultDns}/secrets'

var baseSettings = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
  { name: 'Jwt__SigningKey', value: '@Microsoft.KeyVault(SecretUri=${kvRef}/JwtSigningKey/)' }
  { name: 'Jwt__Issuer', value: 'https://${webAppName}.azurewebsites.net' }
  { name: 'Jwt__Audience', value: 'expovice-app' }
  { name: 'Storage__BlobServiceUri', value: storage.properties.primaryEndpoints.blob }
  { name: 'Database__MigrateOnStartup', value: 'true' }
  { name: 'Database__SeedSampleData', value: 'false' }
]
var smsSettings = smsEnabled
  ? [
      { name: 'Sms__ConnectionString', value: '@Microsoft.KeyVault(SecretUri=${kvRef}/SmsConnectionString/)' }
      { name: 'Sms__From', value: smsFromNumber }
    ]
  : []
var googleSettings = googleEnabled
  ? [
      { name: 'Authentication__Google__ClientId', value: googleClientId }
      { name: 'Authentication__Google__ClientSecret', value: '@Microsoft.KeyVault(SecretUri=${kvRef}/GoogleClientSecret/)' }
    ]
  : []
var appleSettings = appleEnabled
  ? [
      { name: 'Authentication__Apple__ClientId', value: appleClientId }
      { name: 'Authentication__Apple__TeamId', value: appleTeamId }
      { name: 'Authentication__Apple__KeyId', value: appleKeyId }
      { name: 'Authentication__Apple__PrivateKey', value: '@Microsoft.KeyVault(SecretUri=${kvRef}/ApplePrivateKey/)' }
    ]
  : []

resource webApp 'Microsoft.Web/sites@2024-04-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    keyVaultReferenceIdentity: 'SystemAssigned'
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: appServiceSku != 'F1' // Always On is not available on the free tier
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/healthz'
      appSettings: concat(baseSettings, smsSettings, googleSettings, appleSettings)
      connectionStrings: [
        {
          name: 'Sql'
          type: 'SQLAzure'
          // Passwordless: the web app's managed identity is the SQL server's Entra admin.
          connectionString: 'Server=tcp:${sqlFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'
        }
      ]
    }
  }
}

// ---------- Azure SQL (free offer: serverless, auto-pauses when the monthly free allowance is used) ----------

resource sqlServer 'Microsoft.Sql/servers@2024-05-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true // no SQL passwords exist at all
      login: webApp.name
      sid: webApp.identity.principalId
      tenantId: subscription().tenantId
      principalType: 'Application'
    }
  }
}

resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2024-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2024-05-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: { name: 'GP_S_Gen5_2', tier: 'GeneralPurpose', family: 'Gen5', capacity: 2 }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause' // never bills: pauses until next month instead
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368 // 32 GB, the free-offer maximum
  }
}

// ---------- Role assignments for the API's managed identity ----------

resource kvSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, webApp.id, roleKeyVaultSecretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultSecretsUser)
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource blobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, webApp.id, roleStorageBlobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleStorageBlobDataContributor)
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------- Cost guard ----------

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = if (monthlyBudget > 0 && !empty(budgetContactEmails)) {
  name: '${namePrefix}-monthly-budget'
  properties: {
    category: 'Cost'
    amount: monthlyBudget
    timeGrain: 'Monthly'
    timePeriod: { startDate: budgetStartDate }
    notifications: {
      actual80: { enabled: true, operator: 'GreaterThanOrEqualTo', threshold: 80, contactEmails: budgetContactEmails }
      actual100: { enabled: true, operator: 'GreaterThanOrEqualTo', threshold: 100, contactEmails: budgetContactEmails }
    }
  }
}

output apiBaseUrl string = 'https://${webApp.properties.defaultHostName}'
output webAppName string = webApp.name
output sqlServerFqdn string = sqlFqdn
output keyVaultName string = keyVault.name
output storageAccountName string = storage.name
