using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Contracts;
using Elsa.Workflows.Memory;
using Elsa.Workflows.Runtime.Activities;

namespace Elsa.Workflows.IntegrationTests.Activities.Workflows;

public class BreakWhileForkWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder workflow)
    {
        var currentValue = new Variable<int?>
        {
            Name = "CurrentValue",
            Value = 0
        };

        workflow.Root = new Sequence
        {
            Variables = { currentValue },
            Activities =
            {
                While.True(new Fork
                {
                    Branches =
                    {
                        new Sequence
                        {
                            Activities =
                            {
                                new SetVariable
                                {
                                    Variable = currentValue,
                                    Value = new (context => currentValue.Get(context) + 1)
                                },
                                new If(context => currentValue.Get(context) == 3)
                                {
                                    Then = new Break()
                                }
                            }
                        },
                        new Sequence
                        {
                            Activities =
                            {
                                new WriteLine("Waiting for event..."),
                                new Event("Some event") { Id = "SomeEvent" },
                                new WriteLine("Resuming"),
                            }
                        }
                    }
                }),
            }
        };
    }
}