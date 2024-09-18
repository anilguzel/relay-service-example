namespace relayservice
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        private static IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<OmsBusSetting>(hostContext.Configuration.GetSection(nameof(OmsBusSetting)));
                    services.Configure<KafkaSetting>(hostContext.Configuration.GetSection(nameof(KafkaSetting)));

                    services.Configure<ExecutionSetting>(
                        hostContext.Configuration.GetSection(nameof(ExecutionSetting)));
                    services.Configure<DatabaseSetting>(
                        hostContext.Configuration.GetSection(nameof(DatabaseSetting)));

                    services.AddSingleton(provider =>
                        provider.GetRequiredService<IOptions<OmsBusSetting>>().Value);
                    services.AddSingleton(provider =>
                        provider.GetRequiredService<IOptions<KafkaSetting>>().Value);

                    services.AddSingleton<IExecutionSetting>(provider =>
                        provider.GetRequiredService<IOptions<ExecutionSetting>>().Value);
                    services.AddSingleton<IDatabaseSetting>(provider =>
                        provider.GetRequiredService<IOptions<DatabaseSetting>>().Value);
                    services.AddSingleton(provider => provider.GetRequiredService<IOptions<ConsoleLoggerSetting>>().Value);

                    services.AddSingleton<IConsoleLogger, ConsoleLogger>();

                    services.AddMemoryCache();

                    services.AddMassTransit<IOmsBus>(configurator => configurator.AddBus(provider =>
                    {
                        var omsBusSetting = provider.GetRequiredService<OmsBusSetting>();

                        return Bus.Factory.CreateUsingRabbitMq(factoryConfigurator =>
                        {
                            factoryConfigurator.Host(new Uri(omsBusSetting.HostAddress), hostConfigurator =>
                            {
                                hostConfigurator.Username(omsBusSetting.Username);
                                hostConfigurator.Password(omsBusSetting.Password);
                                hostConfigurator.Heartbeat(omsBusSetting.Heartbeat);
                                hostConfigurator.UseCluster(clusterConfigurator =>
                                {
                                    foreach (var busSettingClusterMember in omsBusSetting.ClusterMembers)
                                        clusterConfigurator.Node(busSettingClusterMember);
                                });
                            });
                            factoryConfigurator.ConfigureJsonSerializer(serializerSettings =>
                            {
                                var convertersField = typeof(JsonContractResolver).GetField("_converters",
                                    BindingFlags.NonPublic | BindingFlags.Instance);
                                var newConverters =
                                    (JsonConverter[])convertersField?.GetValue(serializerSettings.ContractResolver);
                                convertersField?.SetValue(serializerSettings.ContractResolver,
                                    newConverters?.Where(t => t.GetType() != typeof(StringDecimalConverter)).ToArray());
                                return serializerSettings;
                            });
                            ConfigureOrderExchanges(factoryConfigurator);
                            ConfigureOrderLineExchanges(factoryConfigurator);
                            ConfigureDeliveryExchanges(factoryConfigurator);
                            ConfigureClaimExchanges(factoryConfigurator);
                        });
                    }));



                    services.AddSingleton<IProducer<string, string>>(provider =>
                    {
                        var omsKafkaSettings = provider.GetRequiredService<KafkaSetting>();

                        var kafkaBrokers = string.Join(",", omsKafkaSettings.ClusterMembers);

                        var config = new ProducerConfig() { BootstrapServers = kafkaBrokers };
                        //config.Acks = Acks.All;

                        var producer = new ProducerBuilder<string, string>(config).Build();

                        return producer;

                    });


                    services.AddSingleton(provider =>
                        new MongoClient(provider.GetRequiredService<IDatabaseSetting>().ConnectionString));
                    services.AddSingleton(provider =>
                        provider.GetRequiredService<MongoClient>()
                            .GetDatabase(provider.GetRequiredService<IDatabaseSetting>().DatabaseName));


                    services.AddSingleton<IMongoCollection<OutboxMessage>>(provider =>
                        provider.GetRequiredService<IMongoDatabase>()
                            .GetCollection<OutboxMessage>(provider.GetRequiredService<IDatabaseSetting>()
                                .CollectionName));


                    services.AddSingleton<IMongoCollection<OutboxMessage>>(provider =>
                        provider.GetRequiredService<IMongoDatabase>()
                            .GetCollection<OutboxMessage>(provider.GetRequiredService<IDatabaseSetting>()
                                .KafkaCollectionName));



                    services.AddSingleton<IMessageBrokerService, RabbitMqMessageBrokerService>();
                    services.AddSingleton<IMessageBrokerService, KafkaMessageBrokerService>();


                    services.AddSingleton<IEventTypeCacheService, EventTypeCacheService>();



                    services.AddScoped<Strategies.OutboxResolver>(serviceProvider => key =>
                    {
                        var databaseSetting = serviceProvider.GetRequiredService<IDatabaseSetting>();
                        IMessageBrokerService messageBrokerService;
                        IMongoCollection<OutboxMessage> mongoCollection;

                        switch (key)
                        {
                            case PublisherType.Rabbit:
                                messageBrokerService = serviceProvider.GetServices<IMessageBrokerService>()
                                    .FirstOrDefault(m => m.PublisherType == PublisherType.Rabbit);
                                mongoCollection = serviceProvider.GetServices<IMongoCollection<OutboxMessage>>()
                                    .FirstOrDefault(c => c.CollectionNamespace.CollectionName == databaseSetting.CollectionName);

                                return (messageBrokerService, mongoCollection);

                            case PublisherType.Kafka:
                                messageBrokerService = serviceProvider.GetServices<IMessageBrokerService>()
                                    .FirstOrDefault(m => m.PublisherType == PublisherType.Kafka);
                                mongoCollection = serviceProvider.GetServices<IMongoCollection<OutboxMessage>>()
                                    .FirstOrDefault(c => c.CollectionNamespace.CollectionName == databaseSetting.KafkaCollectionName);

                                return (messageBrokerService, mongoCollection);

                            default:
                                throw new KeyNotFoundException();
                        }
                    });





                    services.AddHostedService<RabbitMqWorker>();
                    services.AddHostedService<KafkaWorker>();




                });
        }

        private static void ConfigureDeliveryExchanges(IRabbitMqBusFactoryConfigurator cfg)
        {
            #region Oms.Events.V1.Delivery.Created

            cfg.Send<Created>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Created>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Delivery.Deleted

            cfg.Send<Deleted>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Deleted>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Delivery.InvoiceCreated

            cfg.Send<InvoiceCreated>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<InvoiceCreated>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Delivery.Dispatched

            cfg.Send<Dispatched>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Dispatched>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Delivery.Shipped

            cfg.Send<Shipped>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Shipped>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Delivery.Delivered

            cfg.Send<Delivered>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Delivered>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Delivery.Undelivered

            cfg.Send<Undelivered>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Undelivered>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Delivery.Updated

            cfg.Send<Updated>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Updated>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Delivery.IsResendableByDemandUpdated

            cfg.Send<Oms.Events.V1.Delivery.IsResendableByDemandUpdated>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Oms.Events.V1.Delivery.IsResendableByDemandUpdated>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Delivery.Retracted

            cfg.Send<Retracted>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Retracted>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Delivery.CargoChanged

            cfg.Send<CargoChanged>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<CargoChanged>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Delivery.AcceptedByPoint

            cfg.Send<AcceptedByPoint>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<AcceptedByPoint>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Delivery.DeliveredByPoint

            cfg.Send<DeliveredByPoint>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<DeliveredByPoint>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Delivery.DemandAdded

            cfg.Send<DemandAdded>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<DemandAdded>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Delivery.DemandCompleted

            cfg.Send<DemandCompleted>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<DemandCompleted>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Delivery.DemandFailed

            cfg.Send<DemandFailed>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<DemandFailed>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Delivery.EstimatedArrivalDateUpdated

            cfg.Send<EstimatedArrivalDateUpdated>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<EstimatedArrivalDateUpdated>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion


        }

        private static void ConfigureOrderLineExchanges(IRabbitMqBusFactoryConfigurator cfg)
        {
            #region Oms.Events.V1.OrderLine.Created

            cfg.Send<Events.V1.OrderLine.Created>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Events.V1.OrderLine.Created>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.Deleted

            cfg.Send<Events.V1.OrderLine.Deleted>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Events.V1.OrderLine.Deleted>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.InProgress

            cfg.Send<InProgress>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<InProgress>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.Placed

            cfg.Send<Placed>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Placed>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.Cancelled

            cfg.Send<Cancelled>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Cancelled>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.RefundAdded

            cfg.Send<RefundAdded>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<RefundAdded>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.Delivered

            cfg.Send<Events.V1.OrderLine.Delivered>(x =>
            {
                x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message));
            });
            cfg.Publish<Events.V1.OrderLine.Delivered>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.Undelivered

            cfg.Send<Events.V1.OrderLine.Undelivered>(x =>
            {
                x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message));
            });
            cfg.Publish<Events.V1.OrderLine.Undelivered>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.InTransit

            cfg.Send<InTransit>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<InTransit>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.WarehouseUpdated

            cfg.Send<WarehouseUpdated>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<WarehouseUpdated>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.CargoCompanyChanged

            cfg.Send<CargoCompanyChanged>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<CargoCompanyChanged>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.ClaimCreated

            cfg.Send<ClaimCreated>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<ClaimCreated>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.IsClaimableByAdminUpdated

            cfg.Send<IsClaimableByAdminUpdated>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<IsClaimableByAdminUpdated>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.SlotChanged

            cfg.Send<SlotChanged>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<SlotChanged>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.PickingStarted

            cfg.Send<PickingStarted>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<PickingStarted>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.DemandAdded

            cfg.Send<Events.V1.OrderLine.DemandAdded>(x =>
            {
                x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message));
            });
            cfg.Publish<Events.V1.OrderLine.DemandAdded>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.EstimatedArrivalDateUpdated

            cfg.Send<Events.V1.OrderLine.EstimatedArrivalDateUpdated>(x =>
            {
                x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message));
            });
            cfg.Publish<Events.V1.OrderLine.EstimatedArrivalDateUpdated>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion


            #region Oms.Events.V1.OrderLine.DemandCompleted

            cfg.Send<Events.V1.OrderLine.DemandCompleted>(x =>
            {
                x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message));
            });
            cfg.Publish<Events.V1.OrderLine.DemandCompleted>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.OrderLine.DemandFailed

            cfg.Send<Events.V1.OrderLine.DemandFailed>(x =>
            {
                x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message));
            });
            cfg.Publish<Events.V1.OrderLine.DemandFailed>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

        }

        private static void ConfigureOrderExchanges(IRabbitMqBusFactoryConfigurator cfg)
        {
            #region Oms.Events.V1.Order.Created

            cfg.Send<Events.V1.Order.Created>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Events.V1.Order.Created>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Order.ConfirmationConfirmed

            cfg.Send<ConfirmationConfirmed>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<ConfirmationConfirmed>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Order.ConfirmationRejected

            cfg.Send<ConfirmationRejected>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<ConfirmationRejected>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Order.FraudApproved

            cfg.Send<FraudApproved>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<FraudApproved>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Order.FraudRejected

            cfg.Send<FraudRejected>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<FraudRejected>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Order.PaymentReceived

            cfg.Send<PaymentReceived>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<PaymentReceived>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Order.PaymentRejected

            cfg.Send<PaymentRejected>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<PaymentRejected>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Order.ShippingAddressChanged

            cfg.Send<ShippingAddressChanged>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<ShippingAddressChanged>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Order.UserRenewed

            cfg.Send<UserRenewed>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<UserRenewed>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Order.RefundAdded

            cfg.Send<Events.V1.Order.RefundAdded>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Events.V1.Order.RefundAdded>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Order.IsShippingAddressChangeAllowedUpdated

            cfg.Send<IsShippingAddressChangeAllowedUpdated>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<IsShippingAddressChangeAllowedUpdated>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Order.Opened

            cfg.Send<Opened>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Opened>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Order.Closed

            cfg.Send<Closed>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Closed>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Order.TotalAmountChanged

            cfg.Send<TotalAmountChanged>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<TotalAmountChanged>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Order.VisibilityChanged

            cfg.Send<VisibilityChanged>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<VisibilityChanged>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion
        }


        private static void ConfigureClaimExchanges(IRabbitMqBusFactoryConfigurator cfg)
        {
            #region Oms.Events.V1.Claim.Created

            cfg.Send<Events.V1.Claim.Created>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Events.V1.Claim.Created>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Claim.Cancelled

            cfg.Send<Events.V1.Claim.Cancelled>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Events.V1.Claim.Cancelled>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Claim.Rejected

            cfg.Send<Rejected>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Rejected>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Claim.Accepted

            cfg.Send<Accepted>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Accepted>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Claim.AutoActionDecided

            cfg.Send<AutoActionDecided>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<AutoActionDecided>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Claim.MarkedAsAwaitingAction

            cfg.Send<MarkedAsAwaitingAction>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<MarkedAsAwaitingAction>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Claim.MarkedAsInDispute

            cfg.Send<MarkedAsInDispute>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<MarkedAsInDispute>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Claim.Refunded

            cfg.Send<Refunded>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<Refunded>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion

            #region Oms.Events.V1.Claim.PreApprovalConfirmed

            cfg.Send<PreApprovalConfirmed>(x => { x.UseRoutingKeyFormatter(c => RoutingKeyFormat(c.Message)); });
            cfg.Publish<PreApprovalConfirmed>(x =>
                x.ExchangeType = ExchangeType.Topic);

            #endregion
        }


        private static string RoutingKeyFormat(dynamic message)
        {
            return $"Endor.{message.Tenant}.{ObjectId.Parse(message.Id).Increment % 100000}";
        }
    }
}