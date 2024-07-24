# Executing the two examples

For *each* project:

1. Set the secrets:
   - Double click on "Connected Services" for the project in Visual Studio Solution Explorer.
   - Click on "..." button at the right hand side of "secrets.json" -> select "Manage User Secrets" 
   - A new empty "secrets.json" will open. Configure all the secrets as shown below:
	   ```json
	   {
		  "OpenAI:ApiKey": "<api-key-from-openai>",
		  "OpenAI:EmbeddingsDeploymentName": "<deployment-name-for-embeddings-model>",
		  "OpenAI:DeploymentName": "<deployment-name-for-gptXX-model>", 
		  "OpenAI:Endpoint": "https://<azureai-resource-name>.openai.azure.com/"
	   }
	   ```
2. Customize Properties/launchSettings.json to point to a .docx of yours.
3. Run the project.
