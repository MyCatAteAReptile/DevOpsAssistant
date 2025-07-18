using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.PromptTemplates.Handlebars;

string filePath = Path.GetFullPath("appsettings.json");
var config = new ConfigurationBuilder()
    .AddJsonFile(filePath)
    .Build();

var builder = Kernel.CreateBuilder();

builder.AddOpenAIChatCompletion(
    modelId: config["MODEL_ID"]!,
    apiKey: config["PROJECT_KEY"]!,
    endpoint: new Uri(config["PROJECT_ENDPOINT"]!),
    serviceId: config["SERVICE_ID"]!
);

var kernel = builder.Build();

kernel.ImportPluginFromType<DevopsPlugin>();

OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new()
{
    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
};

var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
ChatHistory chatHistory = [];

var deployStageFunction = kernel.CreateFunctionFromPrompt(
promptTemplate: @"This is the most recent build log:
    

 If there are errors, do not deploy the stage environment. Otherwise, invoke the stage deployment function",
functionName: "DeployStageEnvironment",
description: "Deploy the staging environment"
);

kernel.Plugins.AddFromFunctions("DeployStageEnvironment", [deployStageFunction]);

string hbprompt = """
     <message role="system">Instructions: Before creating a new branch for a user, request the new branch name and base branch name/message>
     <message role="user">Can you create a new branch?</message>
     <message role="assistant">Sure, what would you like to name your branch? And which base branch would you like to use?</message>
     <message role="user"></message>
     <message role="assistant">
     """;

var templateFactory = new HandlebarsPromptTemplateFactory();
var promptTemplateConfig = new PromptTemplateConfig()
{
    Template = hbprompt,
    TemplateFormat = "handlebars",
    Name = "CreateBranch",
};

var promptFunction = kernel.CreateFunctionFromPrompt(promptTemplateConfig, templateFactory);
var branchPlugin = kernel.CreatePluginFromFunctions("BranchPlugin", [promptFunction]);
kernel.Plugins.Add(branchPlugin);

kernel.FunctionInvocationFilters.Add(new PermissionFilter());

Console.WriteLine("Press enter to exit");
Console.WriteLine("Assistant: How may I help you?");
Console.Write("User: ");

string input = Console.ReadLine()!;

// User interaction logic

while (input != "") 
{
    chatHistory.AddUserMessage(input);
    await GetReply();
    input = GetInput();
}

string GetInput() 
{
    Console.Write("User: ");
    string input = Console.ReadLine()!;
    chatHistory.AddUserMessage(input);
    return input;
}

async Task GetReply() 
{
    ChatMessageContent reply = await chatCompletionService.GetChatMessageContentAsync(
        chatHistory,
        executionSettings: openAIPromptExecutionSettings,
        kernel: kernel
    );
    Console.WriteLine("Assistant: " + reply.ToString());
    chatHistory.AddAssistantMessage(reply.ToString());
}


class DevopsPlugin
{
    [KernelFunction("BuildStageEnvironment")]
    public string BuildStageEnvironment()
    {
        return "Stage build completed.";
    }

    [KernelFunction("DeployToStage")]
    public string DeployToStage()
    {
        return "Staging site deployed successfully.";
    }

    [KernelFunction("DeployToProd")]
    public string DeployToProd()
    {
        return "Production site deployed successfully.";
    }

    [KernelFunction("CreateNewBranch")]
    public string CreateNewBranch(string branchName, string baseBranch)
    {
        return $"Created new branch `{branchName}` from `{baseBranch}`";
    }

    [KernelFunction("ReadLogFile")]
    public string ReadLogFile()
    {
        string content = File.ReadAllText($"Files/build.log");
        return content;
    }
}

class PermissionFilter : IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        if ((context.Function.PluginName == "DevopsPlugin" && context.Function.Name == "DeployToProd"))
        {
            // Request user approval
            Console.WriteLine("System Message: The assistant requires an approval to complete this operation. Do you approve (Y/N)");
            Console.Write("User: ");
            string shouldProceed = Console.ReadLine()!;

            // Proceed if approved
            if (shouldProceed != "Y")
            {
                context.Result = new FunctionResult(context.Result, "The operation was not approved by the user");
                return;
            }
        }

        await next(context);
    }
}