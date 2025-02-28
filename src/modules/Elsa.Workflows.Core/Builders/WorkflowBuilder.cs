using Elsa.Extensions;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Contracts;
using Elsa.Workflows.Memory;
using Elsa.Workflows.Models;

namespace Elsa.Workflows.Builders;

/// <inheritdoc />
public class WorkflowBuilder : IWorkflowBuilder
{
    private readonly IActivityVisitor _activityVisitor;
    private readonly IIdentityGraphService _identityGraphService;
    private readonly IActivityRegistry _activityRegistry;
    private readonly IIdentityGenerator _identityGenerator;

    /// <summary>
    /// Constructor.
    /// </summary>
    public WorkflowBuilder(IActivityVisitor activityVisitor, IIdentityGraphService identityGraphService, IActivityRegistry activityRegistry, IIdentityGenerator identityGenerator)
    {
        _activityVisitor = activityVisitor;
        _identityGraphService = identityGraphService;
        _activityRegistry = activityRegistry;
        _identityGenerator = identityGenerator;
        Result = new Variable();
    }

    /// <inheritdoc />
    public string? Id { get; set; }

    /// <inheritdoc />
    public string? DefinitionId { get; set; }

    /// <inheritdoc />
    public int Version { get; set; } = 1;

    /// <inheritdoc />
    public string? Name { get; set; }

    /// <inheritdoc />
    public string? Description { get; set; }

    /// <inheritdoc />
    public bool IsReadonly { get; set; }

    /// <inheritdoc />
    public IActivity? Root { get; set; }

    /// <inheritdoc />
    public ICollection<Variable> Variables { get; set; } = new List<Variable>();

    /// <inheritdoc />
    public ICollection<InputDefinition> Inputs { get; set; } = new List<InputDefinition>();

    /// <inheritdoc />
    public ICollection<OutputDefinition> Outputs { get; set; } = new List<OutputDefinition>();

    /// <inheritdoc />
    public ICollection<string> Outcomes { get; set; } = new List<string>();

    /// <inheritdoc />
    public Variable? Result { get; set; }

    /// <inheritdoc />
    public IDictionary<string, object> CustomProperties { get; set; } = new Dictionary<string, object>();

    /// <inheritdoc />
    public WorkflowOptions WorkflowOptions { get; } = new();

    /// <inheritdoc />
    public Variable<T> WithVariable<T>()
    {
        var variable = new Variable<T>();
        Variables.Add(variable);
        variable.WithWorkflowStorage();
        variable.Id = null!; // This ensures that a deterministic ID is assigned by the builder.  
        return variable;
    }

    /// <inheritdoc />
    public Variable<T> WithVariable<T>(string name, T value)
    {
        var variable = WithVariable<T>();
        variable.Name = name;
        variable.Value = value;
        return variable;
    }

    /// <inheritdoc />
    public Variable<T> WithVariable<T>(T value)
    {
        var variable = WithVariable<T>();
        variable.Value = value;
        return variable;
    }

    /// <inheritdoc />
    public IWorkflowBuilder WithVariable<T>(Variable<T> variable)
    {
        Variables.Add(variable);
        return this;
    }

    /// <inheritdoc />
    public IWorkflowBuilder WithVariable(Variable variable)
    {
        Variables.Add(variable);
        return this;
    }

    /// <inheritdoc />
    public IWorkflowBuilder WithVariables(params Variable[] variables)
    {
        foreach (var variable in variables)
            Variables.Add(variable);
        return this;
    }

    /// <inheritdoc />
    public InputDefinition WithInput<T>(string name, string? description = default)
    {
        return WithInput(inputDefinition =>
        {
            inputDefinition.Name = name;

            if (description != null)
                inputDefinition.Description = description;
        });
    }

    /// <inheritdoc />
    public InputDefinition WithInput(string name, Type type, string? description = default)
    {
        return WithInput(inputDefinition =>
        {
            inputDefinition.Name = name;
            inputDefinition.Type = type;

            if (description != null)
                inputDefinition.Description = description;
        });
    }

    /// <inheritdoc />
    public InputDefinition WithInput(string name, Type type, Action<InputDefinition>? setup = default)
    {
        return WithInput(inputDefinition =>
        {
            inputDefinition.Name = name;
            inputDefinition.Type = type;
            setup?.Invoke(inputDefinition);
        });
    }

    /// <inheritdoc />
    public InputDefinition WithInput(Action<InputDefinition> setup)
    {
        var inputDefinition = new InputDefinition();
        setup(inputDefinition);
        Inputs.Add(inputDefinition);
        return inputDefinition;
    }

    /// <inheritdoc />
    public IWorkflowBuilder WithInput(InputDefinition inputDefinition)
    {
        Inputs.Add(inputDefinition);
        return this;
    }

    /// <inheritdoc />
    public IWorkflowBuilder WithCustomProperty(string name, object value)
    {
        CustomProperties[name] = value;
        return this;
    }

    /// <inheritdoc />
    public IWorkflowBuilder WithActivationStrategyType<T>() where T : IWorkflowActivationStrategy
    {
        WorkflowOptions.ActivationStrategyType = typeof(T);
        return this;
    }

    /// <inheritdoc />
    public async Task<Workflow> BuildWorkflowAsync(CancellationToken cancellationToken = default)
    {
        var definitionId = DefinitionId ?? _identityGenerator.GenerateId();
        var id = Id ?? _identityGenerator.GenerateId();
        var root = Root ?? new Sequence();
        var identity = new WorkflowIdentity(definitionId, Version, id);
        var publication = WorkflowPublication.LatestAndPublished;
        var name = string.IsNullOrEmpty(Name) ? definitionId : Name;
        var workflowMetadata = new WorkflowMetadata(name, Description);
        var workflow = new Workflow(identity, publication, workflowMetadata, WorkflowOptions, root, Variables, Inputs, Outputs, Outcomes, CustomProperties, IsReadonly);

        // If a Result variable is defined, install it into the workflow so we can capture the output into it.
        if (Result != null)
        {
            workflow.ResultVariable = Result;
            workflow.Result = new Output<object>(Result);
        }

        var graph = await _activityVisitor.VisitAsync(workflow, cancellationToken);
        var nodes = graph.Flatten().ToList();

        // Register all activity types first. The identity graph service will need to know about all activity types.
        var distinctActivityTypes = nodes.Select(x => x.Activity.GetType()).Distinct().ToList();
        await _activityRegistry.RegisterAsync(distinctActivityTypes, cancellationToken);

        // Assign identities to all activities.
        _identityGraphService.AssignIdentities(nodes);

        // Give unnamed variables in each variable container a predictable name.
        var variableContainers = nodes.Where(x => x.Activity is IVariableContainer).Select(x => (IVariableContainer)x.Activity).ToList();

        foreach (var container in variableContainers)
        {
            var index = 0;
            var unnamedVariables = container.Variables.Where(x => string.IsNullOrWhiteSpace(x.Name)).ToList();

            foreach (var unnamedVariable in unnamedVariables)
                unnamedVariable.Name = $"Variable_{index++}";
        }

        return workflow;
    }

    /// <inheritdoc />
    public async Task<Workflow> BuildWorkflowAsync(IWorkflow workflowDefinition, CancellationToken cancellationToken = default)
    {
        DefinitionId = workflowDefinition.GetType().Name;
        await workflowDefinition.BuildAsync(this, cancellationToken);
        return await BuildWorkflowAsync(cancellationToken);
    }
}