using RabbitMQ.Services;
using RabbitMQ.Services.Demo.Consumers;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddRabbitMQ();
builder.Services.AddConsumerHostedService<DemoMessageA, DemoMessageAHandler>(
    builder.Configuration, options =>
    {
        options.Url = "amqp://v.leschenko:orgEylg9@rabbitmq.vitaliy.org:5672/demo/test";
        options.ConnectionName = "DemoApp";
    });
builder.Services.AddConsumerHostedService<DemoMessageB, DemoMessageBHandler>(
    builder.Configuration, options =>
    {
        options.Url = "amqp://v.leschenko:orgEylg9@rabbitmq.vitaliy.org:5672/demo/test2";
        options.ConnectionName = "DemoApp";
    });

var host = builder.Build();
host.Run();
