# AI Assistant — Owner's Guide

The assistant lets your staff ask RealEstateManager questions by voice or text ("which tenants are overdue?",
"trial balance at 30 September", "how do I clear a PDC?") and have it prepare entries that they confirm with a click.
It uses OpenAI's realtime voice model **gpt-realtime-2.1-mini**.

It is switched **off** until you do the steps below.

## 1. Get an OpenAI API key and cap the spending

1. Go to <https://platform.openai.com>, sign in (or create an account) and add a payment method under **Billing**.
2. Under **Limits**, set a **monthly budget** (for example USD 50). OpenAI stops the assistant when it is reached,
   so a bill can never run away.
3. Under **API keys**, create a key named `RealEstateManager`. Copy it — it starts with `sk-` and is shown only once.

Keep the key secret. Do not email it, paste it in chat, or put it in any file in the project.

## 2. Give the key to the website (SmarterASP)

In the SmarterASP control panel, open the **RealEstateManager** website and add two **environment variables /
secrets** for the application pool:

| Name | Value |
|---|---|
| `Assistant__OpenAIApiKey` | the key from step 1 |
| `Assistant__Enabled` | `true` |

(Two underscores in each name.) Save and **restart** the website. The database password is already stored the same
way, as `ConnectionStrings__DefaultConnection` — leave that one as it is.

To switch the assistant off at any time, set `Assistant__Enabled` to `false` and restart.

## 3. Publish

Publish from Visual Studio as usual. On first start the site adds the assistant's tables by itself (script
`29_AddAiAssistant.sql`) and gives company administrators the new permission.

## 4. Decide who may use it

**System Setup ▸ Users & Permissions**: tick **Use the AI Assistant** for each person who should have it. The assistant can only
see and do what that person's other permissions allow — an accountant without HR rights cannot ask about salaries.

## 5. Test it

1. Sign in, and open a screen. The **Assistant** button appears at the bottom right.
2. Type *"Which invoices are still open?"* and press Send.
3. Select the microphone and say *"How many units are vacant?"*. The browser asks to use the microphone the first
   time — allow it. (The microphone only works on the `https://` address.)
4. Ask it to *"prepare a receipt of 100 against invoice …"* for a test invoice, then press **Cancel** on the card.

## What it costs (estimate)

OpenAI charges by usage. At October 2026 prices for gpt-realtime-2.1-mini (audio: USD 10 per million tokens in,
USD 20 out; text: USD 0.60 in, USD 2.40 out), a spoken conversation costs very roughly **USD 0.05–0.15 per minute**
(about AED 0.20–0.55), and typed chats a small fraction of that. Each session is limited to 15 minutes and each
company to 100 sessions a day; the budget in step 1 is the hard ceiling. Usage per session is recorded in the
`AssistantSessions` table.

## Safety built in

- Nothing is saved without a click on **Confirm**; a spoken "yes" is not enough. Unconfirmed cards expire after 10 minutes.
- It only ever sees the company the user is working in, and only what their permissions allow — checked again on every request.
- It cannot approve contracts or payments, delete or reset posted entries, close periods, or manage users and subscriptions.
- Text written by tenants (service requests) is treated as data, never as instructions.
- Every confirmed action is written to the audit log as `Assistant/<action>`, and the conversation is kept in `AssistantMessages`.
- The OpenAI key never leaves the server; browsers receive a 10-minute key for one session only.
