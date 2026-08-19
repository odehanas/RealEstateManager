# n8n Feedback Notification Setup

This application can send feedback notifications to an `n8n` webhook.

## What The App Sends

When a user submits feedback, the app can post JSON to your webhook with:

- application name
- event type
- company name
- username
- display name
- category
- subject
- message
- submission date

## App Configuration

Update `appsettings.json` or production settings:

```json
"FeedbackNotifications": {
  "Enabled": true,
  "WebhookUrl": "https://your-n8n-domain/webhook/realestate-feedback",
  "SharedSecret": "your-secret-value",
  "ApplicationName": "RealEstateManager"
}
```

## Recommended n8n Flow

1. Create a new workflow.
2. Add a `Webhook` trigger node.
3. Use `POST` method.
4. Copy the production webhook URL.
5. Add an `IF` node to validate the shared secret.
6. Add one or more actions after validation:
   - send email
   - send WhatsApp message
   - create a task in Notion or Trello
   - write to Google Sheets
   - send Microsoft Teams or Slack message
7. Activate the workflow.

## Example Payload

```json
{
  "application": "RealEstateManager",
  "eventType": "feedback.submitted",
  "sharedSecret": "your-secret-value",
  "feedback": {
    "id": 15,
    "companyId": 3,
    "companyName": "Sample Company",
    "appUserId": 12,
    "username": "jane",
    "displayName": "Jane Doe",
    "category": "Feature Request",
    "subject": "Add invoice aging summary",
    "message": "Please add invoice aging by tenant and property.",
    "status": "New",
    "submittedAt": "2026-04-25T08:30:00Z"
  }
}
```

## Suggested First Automation

Best first workflow:

1. Receive feedback from webhook.
2. Check secret.
3. Format a readable message.
4. Send email to your admin mailbox.
5. Append the same record to Google Sheets.

This gives you:

- immediate alert
- historical log outside the app
- simple non-technical review workflow

## Suggested Second Automation

Once stable, add:

- priority routing by category
- automatic WhatsApp or Teams message for `Bug Report`
- weekly summary email for all feedback items

## Security Notes

- Always use a secret value.
- Keep the webhook URL private.
- Use HTTPS only.
- If possible, filter or reject requests with missing or wrong secret values in `n8n`.
