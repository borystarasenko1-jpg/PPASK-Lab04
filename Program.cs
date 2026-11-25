using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Protocol;

namespace BuildingControl
{
    public sealed class TemperatureSensor
    {
        public double Value { get; set; }

        public double ReadValue()
        {
            return Value;
        }
    }

    public sealed class HumiditySensor
    {
        public double Value { get; set; }

        public double ReadValue()
        {
            return Value;
        }
    }

    public sealed class MotionSensor
    {
        public bool Motion { get; set; }

        public bool ReadValue()
        {
            return Motion;
        }
    }

    public sealed class Device
    {
        public string Name { get; private set; }
        public bool IsOn { get; private set; }

        public Device(string name)
        {
            Name = name;
        }

        public void SetState(bool isOn)
        {
            IsOn = isOn;
        }

        public void ShowState()
        {
            Console.WriteLine("{0} — {1}", Name, IsOn ? "увімкнено" : "вимкнено");
        }
    }

    public sealed class BuildingController
    {
        private const double TemperatureLimit = 21.0;
        private const double HumidityLimit = 60.0;
        private readonly TemperatureSensor temperature;
        private readonly HumiditySensor humidity;
        private readonly MotionSensor motion;
        private readonly Device heating;
        private readonly Device ventilation;
        private readonly Device lighting;

        public BuildingController(TemperatureSensor temperature, HumiditySensor humidity,
            MotionSensor motion, Device heating, Device ventilation, Device lighting)
        {
            this.temperature = temperature;
            this.humidity = humidity;
            this.motion = motion;
            this.heating = heating;
            this.ventilation = ventilation;
            this.lighting = lighting;
        }

        public void CheckBuilding()
        {
            heating.SetState(temperature.ReadValue() < TemperatureLimit);
            ventilation.SetState(humidity.ReadValue() > HumidityLimit);
            lighting.SetState(motion.ReadValue());
        }
    }

    internal static class Program
    {
        private static async Task Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            var temperature = new TemperatureSensor();
            var humidity = new HumiditySensor();
            var motion = new MotionSensor();
            var heating = new Device("Опалення");
            var ventilation = new Device("Вентиляція");
            var lighting = new Device("Освітлення");
            var controller = new BuildingController(
                temperature, humidity, motion, heating, ventilation, lighting);

            var factory = new MqttClientFactory();
            using var client = factory.CreateMqttClient();
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer("127.0.0.1", 1883)
                .Build();
            await client.ConnectAsync(options);

            var readings = new[]
            {
                new { Temperature = 19.5, Humidity = 67.0, Motion = true },
                new { Temperature = 23.0, Humidity = 50.0, Motion = false },
                new { Temperature = 21.0, Humidity = 60.0, Motion = true }
            };

            for (int i = 0; i < 10; i++)
            {
                var sample = readings[i % readings.Length];
                temperature.Value = sample.Temperature;
                humidity.Value = sample.Humidity;
                motion.Motion = sample.Motion;
                controller.CheckBuilding();

                var measuredAtUtc = DateTimeOffset.UtcNow;
                await Publish(client, "temperature", temperature.ReadValue(), "°C", measuredAtUtc);
                await Publish(client, "humidity", humidity.ReadValue(), "%", measuredAtUtc);
                await Publish(client, "motion", motion.ReadValue(), "bool", measuredAtUtc);

                Console.WriteLine(string.Format(CultureInfo.GetCultureInfo("uk-UA"),
                    "Дослід {0}. Температура {1:0.0} °C, вологість {2:0} %, рух {3}.",
                    i + 1, temperature.Value, humidity.Value, motion.Motion ? "є" : "відсутній"));
                heating.ShowState();
                ventilation.ShowState();
                lighting.ShowState();
                Console.WriteLine();
                if (i < 9)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
            await client.DisconnectAsync();
        }

        private static async Task Publish<T>(IMqttClient client, string sensor, T value,
            string unit, DateTimeOffset measuredAtUtc)
        {
            var payload = JsonSerializer.Serialize(new { value, unit, measuredAtUtc });
            var message = new MqttApplicationMessageBuilder()
                .WithTopic("building/1/sensors/" + sensor)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();
            await client.PublishAsync(message);
        }
    }
}
