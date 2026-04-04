using System;
using System.Collections.Generic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;
using DwarfFortress.GameLogic.Systems;

namespace DwarfFortress.GameLogic.Jobs;

internal readonly struct JobActionExecutionContext
{
    public JobSystem Owner { get; }
    public GameContext Context { get; }
    public Job Job { get; }
    public Queue<ActionStep> Steps { get; }
    public EntityRegistry Registry { get; }
    public float Delta { get; }

    public JobActionExecutionContext(
        JobSystem owner,
        GameContext context,
        Job job,
        Queue<ActionStep> steps,
        EntityRegistry registry,
        float delta)
    {
        Owner = owner;
        Context = context;
        Job = job;
        Steps = steps;
        Registry = registry;
        Delta = delta;
    }
}

internal sealed class JobActionExecutor
{
    private readonly Dictionary<Type, IJobActionHandler> _handlers = new()
    {
        [typeof(WorkAtStep)] = new WorkAtStepHandler(),
        [typeof(WaitStep)] = new WaitStepHandler(),
        [typeof(MoveToStep)] = new MoveToStepHandler(),
        [typeof(PickUpItemStep)] = new PickUpItemStepHandler(),
        [typeof(PlaceItemStep)] = new PlaceItemStepHandler(),
    };

    private readonly IJobActionHandler _defaultHandler = new DefaultActionHandler();

    public void Execute(JobActionExecutionContext context, ActionStep step)
    {
        if (!_handlers.TryGetValue(step.GetType(), out var handler))
            handler = _defaultHandler;

        handler.Execute(context, step);
    }

    private interface IJobActionHandler
    {
        void Execute(JobActionExecutionContext context, ActionStep step);
    }

    private abstract class JobActionHandler<TStep> : IJobActionHandler
        where TStep : ActionStep
    {
        public void Execute(JobActionExecutionContext context, ActionStep step)
            => Execute(context, (TStep)step);

        protected abstract void Execute(JobActionExecutionContext context, TStep step);
    }

    private sealed class WorkAtStepHandler : JobActionHandler<WorkAtStep>
    {
        protected override void Execute(JobActionExecutionContext context, WorkAtStep step)
        {
            if (!context.Owner.EnsureWorkPosition(context.Job, step, context.Steps, context.Registry))
            {
                context.Owner.StopWorkAnimation(context.Job);
                return;
            }

            context.Owner.StartWorkAnimation(context.Job, step);
            context.Job.WorkProgress += context.Delta;
            if (context.Job.WorkProgress >= step.Duration)
            {
                context.Owner.StopWorkAnimation(context.Job);
                context.Steps.Dequeue();
                context.Job.WorkProgress = 0f;
            }
        }
    }

    private sealed class WaitStepHandler : JobActionHandler<WaitStep>
    {
        protected override void Execute(JobActionExecutionContext context, WaitStep step)
        {
            context.Owner.StopWorkAnimation(context.Job);
            context.Job.WorkProgress += context.Delta;
            if (context.Job.WorkProgress >= step.Duration)
            {
                context.Steps.Dequeue();
                context.Job.WorkProgress = 0f;
            }
        }
    }

    private sealed class MoveToStepHandler : JobActionHandler<MoveToStep>
    {
        protected override void Execute(JobActionExecutionContext context, MoveToStep step)
        {
            context.Owner.StopWorkAnimation(context.Job);
            context.Owner.TickMoveStep(context.Job, step, context.Delta, context.Steps, context.Registry);
        }
    }

    private sealed class PickUpItemStepHandler : JobActionHandler<PickUpItemStep>
    {
        protected override void Execute(JobActionExecutionContext context, PickUpItemStep step)
        {
            context.Owner.StopWorkAnimation(context.Job);

            var itemSystem = context.Context.TryGet<ItemSystem>();
            var entity = context.Registry.TryGetById(context.Job.AssignedDwarfId);
            if (itemSystem is not null && entity is not null)
            {
                var carrierPos = entity.Components.Get<PositionComponent>().Position;
                if (!itemSystem.PickUpItem(step.ItemEntityId, context.Job.AssignedDwarfId, carrierPos, step.CarryMode))
                {
                    context.Owner.FailJob(context.Job, "pickup_failed");
                    return;
                }
            }

            context.Steps.Dequeue();
        }
    }

    private sealed class PlaceItemStepHandler : JobActionHandler<PlaceItemStep>
    {
        protected override void Execute(JobActionExecutionContext context, PlaceItemStep step)
        {
            context.Owner.StopWorkAnimation(context.Job);

            var itemSystem = context.Context.TryGet<ItemSystem>();
            if (itemSystem is not null)
            {
                if (step.ContainerBuildingId >= 0)
                    itemSystem.StoreItemInBuilding(step.ItemEntityId, step.ContainerBuildingId, step.Target);
                else
                    itemSystem.MoveItem(step.ItemEntityId, step.Target);
            }

            context.Steps.Dequeue();
        }
    }

    private sealed class DefaultActionHandler : IJobActionHandler
    {
        public void Execute(JobActionExecutionContext context, ActionStep step)
        {
            context.Owner.StopWorkAnimation(context.Job);
            context.Steps.Dequeue();
        }
    }
}