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
        //���������� ����������� ���������� ��� ������ � ������, ����� � ���
        private static GpioController s_GpioController;
        private static GpioPin LedB;
        private static PwmChannel LedG;
        private static GpioPin buttonPin;
        private static bool lastBtnState = false;
        private static AdcController adcController;
        private static AdcChannel adcChannel;
        public static void Main()
        {
            //����������� ������ ����� GpioController ��� ���������� ������ (� ���� ������ - ����� �����)
            s_GpioController = new GpioController();
            //������������� ����� ��� �� 23 ���� - ����������
            Configuration.SetPinFunction(23, DeviceFunction.PWM1);
            //������� ���-����� ��� 23 ����, � ��������� ��� ������� ���������� �� �����
            LedG = PwmChannel.CreateFromPin(23, 40000, 0);
            LedB = s_GpioController.OpenPin(2, PinMode.Output);
            //��������� ��� ������ �� ����/���������� �������
            buttonPin = s_GpioController.OpenPin(0, PinMode.Input);
            //��������� ������ ������ ��� ���������� ��� � ��������� ����� �� ���������� �������� ����������
            adcController = new AdcController();
            adcChannel = adcController.OpenChannel(Gpio.IO04);
            //��������� ���������� �������� ����������
            int analogValue = adcChannel.ReadValue();
            double dutyCycle = (double)analogValue / 4095;
            //����� ��������� - ��������, � �������� ����������� �������� dutyCycle
            LedB.Write(PinValue.Low);
            LedG.DutyCycle = dutyCycle;
            //������� �������� ����������� ����� ��� ���������� �������� ����������� �� �������, ����
            //��� �� ����� ������������, ������� Connect() ��������� �� � ��������� ����������
            bool ledHigh = false;
            TelegramBot.Connect();

            while (true)
            {
                //��������� ���������� ButtonState ����������� �������� true, ���� ������ ���� ������
                bool buttonState = (buttonPin.Read() == PinValue.Low);

                try
                {
                    //���� ������� ���������� �� ���� ������������ ����� ����������� ������ �������
                    if (ledHigh == false)
                    {
                        if (LedG.DutyCycle < 0.1024)
                        {
                            //������� ����� ��, �������� �������� ���������� ������������� �� ����������� � ��������
                            //����� ����������� � ��������� ��� � ���������� �������� ����������
                            analogValue = adcChannel.ReadValue();
                            dutyCycle = (double)analogValue / 4095;
                            LedG.DutyCycle = dutyCycle;

                            if (dutyCycle == 0)
                            {
                                //���� ���������� �������, ����������� ����������
                                throw new ZeroVoltageException("Zero voltage detected!");
                            }

                            if (dutyCycle < 1)
                            {
                                //���� ���������� �� �������, ����������� ��������������� ����������
                                throw new LowVoltageException("Low voltage detected!");
                            }
                        }
                        else
                        {
                            //��� ������ ���������� ������������, ����������� ������������ �������� ���,
                            //�������� �������� ����������� �������� true
                            ledHigh = true;
                            LedG.DutyCycle = 1;
                        }
                    }
                    else
                    {
                        /* ����� ������� ������ ����������� �������� �������� if
                         * � ����������� ����� ����������� � ������� ������� LedToggle
                         * ���������: ������ �������� �������� ���������
                         * ��� �������������� ��������� ���������,
                         * ���� �� ������ ������, �� ��� ������ �������
                         * ��������� ��������� ��������� ���� ���� ���,
                         * ���� ������ ��� ������� � �������� ������ ����������,
                         * �� ���������� ����� ������������� ��� ������� ������ � ��������
                         * ������ �� ~40 ��� (�� ���� ������� ������ �����, ������� ������ ������� ��
                         * ���� ������ ������)
                        */

                        if (buttonState != lastBtnState)
                        {
                            if (buttonState)
                            {
                                //������� ����� �����������
                                LedToggle();
                            }
                        }
                    }
                }
                catch (ZeroVoltageException ex)
                {
                    //���������� � ��������� ���������� ��������� ������������ ������ ����������
                    string exceptionMessage = ex.ZeroVoltageHandler();
                    //���������� ��� ��������� � ����� ������ TelegramBot, ������� ���������� ������������ � ���������
                    //��������� � �������� ����������
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
            //���� ��� �� 23 ����, � �������� ��������� ���������, ����������, �� ����������� ��������� �����������
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
        //� ������ ������ ����������� ����������� � Wi-Fi � �������� ��������� ����� ���������

        //����������� ���������� ��� ������ � ���������-����� � �����
        const string MYSSID = "Redmi 9";
        const string MYPASSWORD = "123123123";
        const string ChatId = "964593325";
        const string BotToken = "6936995532:AAGBVjV4A0Lju2Wq5QFEIYi4-nt0_vsiufc";
        public static void Connect()
        {
            try
            {
                //������� � ���������� ������ ���-��� (�� ������������) ������ �� ���������������� 
                WifiAdapter wifi = WifiAdapter.FindAllAdapters()[0];
                //����������� ������� AvailableNetworksChanged, ����� ��� ����������� ����� ���������� ������������ 
                wifi.AvailableNetworksChanged += Wifi_AvailableNetworksChanged;

                Thread.Sleep(5_000);

                Debug.WriteLine("starting Wi-Fi scan");
                //���������� ��������� ���-��� ���� ����������
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
            //�������� � report ������ ����� � �� �������� ���������
            WifiNetworkReport report = sender.NetworkReport;

            foreach (WifiAvailableNetwork net in report.AvailableNetworks)
            {
                //���� ssid ��������� ���� ��������� � ���������, �� �� ������������ � ���
                if (net.Ssid == MYSSID)
                {
                    //����������, ���� ��� ���������� � �����-���� ����
                    sender.Disconnect();
                    //������� �������������
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
            //�� ������ ������ ����� �������������� ���-������
            string apiUrl = $"https://api.telegram.org/bot{BotToken}/sendMessage?chat_id={ChatId}&text={message}";
            //��������� ����� HttpClient
            using (HttpClient client = new HttpClient())
            {
                //����������� ��������� ���������� �����, ��� ���������� ��������� ��������
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
                    //������� ������������, using ���������� ������������, ��� ��� response �� ���������
                    //����� ������, � ����������� � ��������������� �����, ������������� using �������� �������� �������� �������������� �����
                    //�� ������� ������ ����� ��������
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
        //������ �����������
        public ZeroVoltageException(string message) : base(message)
        {
        }
        //����� ���������� ������ � �������
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
