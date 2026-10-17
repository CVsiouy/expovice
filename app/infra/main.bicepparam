using './main.bicep'

// Non-secret values only. Secrets are read from environment variables at deploy time so they never land in git:
//   export EXPOVICE_JWT_KEY=$(openssl rand -base64 48)
param jwtSigningKey = readEnvironmentVariable('EXPOVICE_JWT_KEY')
param googleClientSecret = readEnvironmentVariable('EXPOVICE_GOOGLE_SECRET', '')
param applePrivateKey = readEnvironmentVariable('EXPOVICE_APPLE_P8', '')

param appServiceSku = 'F1'
param smsFromNumber = ''
param googleClientId = ''
param appleClientId = ''
param appleTeamId = ''
param appleKeyId = ''
param monthlyBudget = 500
param budgetContactEmails = []
