using System;
using GreenPipes;
using MassTransit;
using MassTransit.Azure.ServiceBus.Core;
using MassTransit.ConsumeConfigurators;
using MassTransit.Definition;
using MassTransit.JobService;


namespace JobService.Components
{
    public class ConvertVideoJobConsumerDefinition :
        ConsumerDefinition<ConvertVideoJobConsumer>
    {
        protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator,
            IConsumerConfigurator<ConvertVideoJobConsumer> consumerConfigurator)
        {
            consumerConfigurator.Options<JobOptions<ConvertVideo>>(options =>
                options.SetRetry(r => r.Interval(3, TimeSpan.FromSeconds(30))).SetJobTimeout(TimeSpan.FromMinutes(10)).SetConcurrentJobLimit(10));
            
            if ( endpointConfigurator is IServiceBusReceiveEndpointConfigurator sbc ) sbc.RequiresSession = true;
        }
    }
}