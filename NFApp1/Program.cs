using System;
using System.Diagnostics;
using System.Threading;
using System.Net.Http;
using System.Device.Gpio;
using System.Device.Adc;
using nanoFramework.Hardware.Esp32;
using System.Device.Pwm;
using System.Device.Wifi;
using static System.Math;


namespace NFApp1
{
    public class Program
    {
        //Объявление статических переменных для работы с пинами, ШИМом и АЦП
        private static GpioController s_GpioController;
        private static GpioPin LedB;
        private static PwmChannel LedG;
        private static GpioPin buttonPin;
        private static bool lastBtnState = false;
        private static AdcController adcController;
        private static AdcChannel adcChannel;
        public static void Main()
        {
            //объявляется объект класс GpioController для управления пинами (в моем случае - одним пином)
            s_GpioController = new GpioController();
            //Конфигурирует канал ШИМ на 23 пине - светодиоде
            Configuration.SetPinFunction(23, DeviceFunction.PWM1);
            //Создаем ШИП-канал для 23 пина, и открываем пин второго светодиода на выход
            LedG = PwmChannel.CreateFromPin(23, 40000, 0);
            LedB = s_GpioController.OpenPin(2, PinMode.Output);
            //объявляем пин кнопки на вход/считывание сигнала
            buttonPin = s_GpioController.OpenPin(0, PinMode.Input);
            //объявляем объект класса для управления АЦП и открываем канал на считывание опорного напряжения
            adcController = new AdcController();
            adcChannel = adcController.OpenChannel(Gpio.IO04);
            //первичное считывание опорного напряжения
            int analogValue = adcChannel.ReadValue();
            double dutyCycle = (double)analogValue / 4095;
            //Синий светодиод - выключен, а зеленому присваиваем значение dutyCycle
            LedB.Write(PinValue.Low);
            LedG.DutyCycle = dutyCycle;
            //булевое значение использется далее дял считывания опорного напряджения до момента, пока
            //оно не будет максимальным, функция Connect() соединяет МК с мобильным интернетом
            bool ledHigh = false;
            TelegramBot.Connect();

            while (true)
            {
                //локальной переменной ButtonState присваиваем значение true, если кнопка была нажата
                bool buttonState = (buttonPin.Read() == PinValue.Low);

                try
                {
                    //пока опорное напряжение не бует максимальным будет выполняться данное условие
                    if (ledHigh == false)
                    {
                        if (LedG.DutyCycle < 0.1024)
                        {
                            //принцип такой же, значение опроного напряжение преобразуется из аналогового в цифровое
                            //Потом переводится в интервалы ШИМ и передаются зеленому светодиоду
                            analogValue = adcChannel.ReadValue();
                            dutyCycle = (double)analogValue / 4095;
                            LedG.DutyCycle = dutyCycle;

                            if (dutyCycle == 0)
                            {
                                //если напряжение нулевое, срабатывает исключение
                                throw new ZeroVoltageException("Zero voltage detected!");
                            }

                            if (dutyCycle < 1)
                            {
                                //если напряжения не хватает, срабатывает соответствующее исключение
                                throw new LowVoltageException("Low voltage detected!");
                            }
                        }
                        else
                        {
                            //как только напряжение максимальное, присваиваем максимальное значение ШИМ,
                            //булевому значению присваиваем значение true
                            ledHigh = true;
                            LedG.DutyCycle = 1;
                        }
                    }
                    else
                    {
                        /* после нажатия кнопки срабатывает условный оператор if
                         * и выполняется смена светодиодов с помощью функции LedToggle
                         * УТОЧНЕНИЕ: первый условный оператор необходим
                         * для предотвращения замыкания контактов,
                         * если мы зажмем кнопку, то код внутри первого
                         * условного оператора сработает лишь один раз,
                         * если убрать это условие и оставить только внутреннее,
                         * то светодиоды будут переключаться при нажатии кнопки с частотой
                         * работы МК ~40 Мгц (то есть столько проход цикла, сколько успеет сделать МК
                         * пока нажата кнопка)
                        */

                        if (buttonState != lastBtnState)
                        {
                            if (buttonState)
                            {
                                //функция смена светодиодов
                                LedToggle();
                            }
                        }
                    }
                }
                catch (ZeroVoltageException ex)
                {
                    //записываем в строковую переменную результат срабатывания метода исключения
                    string exceptionMessage = ex.ZeroVoltageHandler();
                    //отправляем это сообщение в метод класса TelegramBot, который отправляет пользователю в телеграмм
                    //сообщение о нехватке напряжения
                    TelegramBot.Messaging(exceptionMessage);
                    Thread.Sleep(1000);
                }
                catch (LowVoltageException ex)
                {
                    string exceptionMessage = ex.LowVoltageHandler();
                    TelegramBot.Messaging(exceptionMessage);
                    Thread.Sleep(1000);
                }
                lastBtnState = buttonState;

                Thread.Sleep(100);
            }

        }

        public static void LedToggle()
        {
            //если ШИМ на 23 пине, к которому подключен светодиод, максимален, то инвертируем состояния светодиодов
            if (LedG.DutyCycle == 0.1024)
            {
                LedB.Write(PinValue.High);
                LedG.DutyCycle = 0;
            }
            else
            {
                LedG.DutyCycle = 1;
                LedB.Write(PinValue.Low);
            }
        }
    }
    class TelegramBot
    {
        //в данном классе реализовано подключение к Wi-Fi и отправка сообщений через телеграмм

        //Объявляются переменные для работы с телеграмм-ботом и сетью
        const string MYSSID = "Redmi 9";
        const string MYPASSWORD = "123123123";
        // Ниже введите свой user_id
        const string ChatId = "";
        const string BotToken = "6936995532:AAGBVjV4A0Lju2Wq5QFEIYi4-nt0_vsiufc";
        public static void Connect()
        {
            try
            {
                //находит и записывает первый вай-фай (он единственный) модуль на микроконтроллере 
                WifiAdapter wifi = WifiAdapter.FindAllAdapters()[0];
                //настраиваем событие AvailableNetworksChanged, чтобы оно срабатывало после завершения сканирования 
                wifi.AvailableNetworksChanged += Wifi_AvailableNetworksChanged;

                Thread.Sleep(5_000);

                Debug.WriteLine("starting Wi-Fi scan");
                //асинхронно сканируем вай-фай сети поблизости
                wifi.ScanAsync();

                Thread.Sleep(15000);

            }
            catch (Exception ex)
            {
                Debug.WriteLine("message:" + ex.Message);
                Debug.WriteLine("stack:" + ex.StackTrace);
            }
        }

        private static void Wifi_AvailableNetworksChanged(WifiAdapter sender, object e)
        {
            Debug.WriteLine("Wifi_AvailableNetworksChanged - get report");
            //получаем в report список сетей и их основные параметры
            WifiNetworkReport report = sender.NetworkReport;

            foreach (WifiAvailableNetwork net in report.AvailableNetworks)
            {
                //если ssid найденной сети совпадает с указанной, то мы подключаемся к ней
                if (net.Ssid == MYSSID)
                {
                    //отклчаемся, если уже подключены к какой-либо сети
                    sender.Disconnect();
                    //пробуем подкелючиться
                    WifiConnectionResult result = sender.Connect(net, WifiReconnectionKind.Automatic, MYPASSWORD);

                    if (result.ConnectionStatus == WifiConnectionStatus.Success)
                    {
                        Debug.WriteLine("Connected to Wifi network");
                    }
                    else
                    {
                        Debug.WriteLine($"Error {result.ConnectionStatus.ToString()} connecting o Wifi network");
                    }
                }
            }
        }

        public static void Messaging(string message)
        {
            //на данную ссылку юужет осуществляться гет-запрос
            string apiUrl = $"https://api.telegram.org/bot{BotToken}/sendMessage?chat_id={ChatId}&text={message}";
            //объявляем новый HttpClient
            using (HttpClient client = new HttpClient())
            {
                //ОБЯЗАТЕЛЬНО указываем сертификат сайта, его необходимо указывать напрямую
                client.HttpsAuthentCert = new System.Security.Cryptography.X509Certificates.X509Certificate(
                @"-----BEGIN CERTIFICATE-----
MIIEADCCAuigAwIBAgIBADANBgkqhkiG9w0BAQUFADBjMQswCQYDVQQGEwJVUzEh
MB8GA1UEChMYVGhlIEdvIERhZGR5IEdyb3VwLCBJbmMuMTEwLwYDVQQLEyhHbyBE
YWRkeSBDbGFzcyAyIENlcnRpZmljYXRpb24gQXV0aG9yaXR5MB4XDTA0MDYyOTE3
MDYyMFoXDTM0MDYyOTE3MDYyMFowYzELMAkGA1UEBhMCVVMxITAfBgNVBAoTGFRo
ZSBHbyBEYWRkeSBHcm91cCwgSW5jLjExMC8GA1UECxMoR28gRGFkZHkgQ2xhc3Mg
MiBDZXJ0aWZpY2F0aW9uIEF1dGhvcml0eTCCASAwDQYJKoZIhvcNAQEBBQADggEN
ADCCAQgCggEBAN6d1+pXGEmhW+vXX0iG6r7d/+TvZxz0ZWizV3GgXne77ZtJ6XCA
PVYYYwhv2vLM0D9/AlQiVBDYsoHUwHU9S3/Hd8M+eKsaA7Ugay9qK7HFiH7Eux6w
wdhFJ2+qN1j3hybX2C32qRe3H3I2TqYXP2WYktsqbl2i/ojgC95/5Y0V4evLOtXi
EqITLdiOr18SPaAIBQi2XKVlOARFmR6jYGB0xUGlcmIbYsUfb18aQr4CUWWoriMY
avx4A6lNf4DD+qta/KFApMoZFv6yyO9ecw3ud72a9nmYvLEHZ6IVDd2gWMZEewo+
YihfukEHU1jPEX44dMX4/7VpkI+EdOqXG68CAQOjgcAwgb0wHQYDVR0OBBYEFNLE
sNKR1EwRcbNhyz2h/t2oatTjMIGNBgNVHSMEgYUwgYKAFNLEsNKR1EwRcbNhyz2h
/t2oatTjoWekZTBjMQswCQYDVQQGEwJVUzEhMB8GA1UEChMYVGhlIEdvIERhZGR5
IEdyb3VwLCBJbmMuMTEwLwYDVQQLEyhHbyBEYWRkeSBDbGFzcyAyIENlcnRpZmlj
YXRpb24gQXV0aG9yaXR5ggEAMAwGA1UdEwQFMAMBAf8wDQYJKoZIhvcNAQEFBQAD
ggEBADJL87LKPpH8EsahB4yOd6AzBhRckB4Y9wimPQoZ+YeAEW5p5JYXMP80kWNy
OO7MHAGjHZQopDH2esRU1/blMVgDoszOYtuURXO1v0XJJLXVggKtI3lpjbi2Tc7P
TMozI+gciKqdi0FuFskg5YmezTvacPd+mSYgFFQlq25zheabIZ0KbIIOqPjCDPoQ
HmyW74cNxA9hi63ugyuV+I6ShHI56yDqg+2DzZduCLzrTia2cyvk0/ZM/iZx4mER
dEr/VxqHD3VILs9RaRegAhJhldXRQLIQTO7ErBBDpqWeCtWVYpoNz4iCxTIM5Cuf
ReYNnyicsbkqWletNw+vHX/bvZ8=
-----END CERTIFICATE-----");
                try
                {
                    //пробуем подключиться, using необходимо использовать, так как response не пропадает
                    //после вызова, а сохраняется в соответствующем стеке, использование using помогает избежать проблемы перезаполнения стека
                    //он удаляет запрос после отправки
                    using HttpResponseMessage response = client.Get(apiUrl);

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex}");
                }
            }
        }
    }

    public class ZeroVoltageException : Exception
    {
        //пустой конструктор
        public ZeroVoltageException(string message) : base(message)
        {
        }
        //метод возвращает строку с ошибкой
        public string ZeroVoltageHandler()
        {
            return "Error: zero voltage for green LED!";
        }
    }

    public class LowVoltageException : Exception
    {
        public LowVoltageException(string message) : base(message)
        {
        }

        public string LowVoltageHandler()
        {
            return "Error: too little voltage is applied to the green LED";
        }
    }
}
