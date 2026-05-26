# Teams status to MQTT
This is just a small tool to get the status of a user in a Microsoft Teams meeting and send it to MQTT.

It requires to be registered in yout Azure App Registration, but then each user can use their own Tenant and Client ID to request their status.


## Azure App Registration setup

1. Go to Azure Portal.
2. Open Microsoft Entra ID.
3. Go to App registrations.
4. Create a new app registration.
5. Supported account type can usually be:
   * Single tenant, if only your organization uses it.
   
6. Copy:
   * Application client ID
   * Directory tenant ID
7. Go to Authentication.
8. Enable:
   * Allow public client flows
9. Go to API permissions.
10. Add Microsoft Graph delegated permissions:
  * User.Read
  * Presence.Read
11. Grant admin consent if your tenant requires it.
