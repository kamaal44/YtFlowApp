﻿using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using ZXing;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace YtFlow.App.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class QrCodePage : Page
    {
        private Result result;
        private readonly MediaCapture mediaCapture = new MediaCapture();
        private DispatcherTimer timer;
        private bool isBusy;

        public QrCodePage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            InitVideoCapture();
            InitVideoTimer();
        }

        protected async override void OnNavigatedFrom(NavigationEventArgs e)
        {
            timer.Stop();
            if (isBusy)
            {
                await mediaCapture.StopPreviewAsync();
            }
            isBusy = false;
        }

        private void InitVideoTimer()
        {
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(3);
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        private async void Timer_Tick(object sender, object e)
        {
            try
            {
                if (!isBusy)
                {
                    isBusy = true;
                    var stream = new InMemoryRandomAccessStream();
                    await mediaCapture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), stream);
                    var writeableBmp = await ReadBitmap(stream, ".jpg");
                    await Task.Factory.StartNew(async () => { await ScanBitmap(writeableBmp); });
                }
                isBusy = false;
                await Task.Delay(100);
            }
            catch (Exception)
            {
                isBusy = false;
            }
        }

        private static Guid DecoderIDFromFileExtension(string strExtension)
        {
            Guid encoderId;
            switch (strExtension.ToLower())
            {
                case ".jpg":
                case ".jpeg":
                    encoderId = BitmapDecoder.JpegDecoderId;
                    break;

                case ".bmp":
                    encoderId = BitmapDecoder.BmpDecoderId;
                    break;

                case ".png":
                default:
                    encoderId = BitmapDecoder.PngDecoderId;
                    break;
            }
            return encoderId;
        }

        public static Size MaxSizeSupported = new Size(4000, 3000);

        /// <summary>
        /// 读取照片流 转为WriteableBitmap给二维码解码器
        /// </summary>
        /// <param name="fileStream"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public async static Task<WriteableBitmap> ReadBitmap(IRandomAccessStream fileStream, string type)
        {
            var decoderId = DecoderIDFromFileExtension(type);
            var decoder = await BitmapDecoder.CreateAsync(decoderId, fileStream);
            var tf = new BitmapTransform();

            var width = decoder.OrientedPixelWidth;
            var height = decoder.OrientedPixelHeight;
            if (decoder.OrientedPixelWidth > MaxSizeSupported.Width || decoder.OrientedPixelHeight > MaxSizeSupported.Height)
            {
                var dScale = Math.Min(MaxSizeSupported.Width / decoder.OrientedPixelWidth, MaxSizeSupported.Height / decoder.OrientedPixelHeight);
                width = (uint)(decoder.OrientedPixelWidth * dScale);
                height = (uint)(decoder.OrientedPixelHeight * dScale);
                tf.ScaledWidth = (uint)(decoder.PixelWidth * dScale);
                tf.ScaledHeight = (uint)(decoder.PixelHeight * dScale);
            }
            var bitmap = new WriteableBitmap((int)width, (int)height);
            var dataprovider = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, tf,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
            var pixels = dataprovider.DetachPixelData();
            var pixelStream2 = bitmap.PixelBuffer.AsStream();
            pixelStream2.Write(pixels, 0, pixels.Length);

            return bitmap;
        }

        /// <summary>
        /// 解析二维码图片
        /// </summary>
        /// <param name="writeableBmp">图片</param>
        /// <returns></returns>
        private async Task ScanBitmap(WriteableBitmap writeableBmp)
        {
            var barcodeReader = new BarcodeReader
            {
                AutoRotate = true,
                Options = new ZXing.Common.DecodingOptions { TryHarder = true }
            };
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                result = barcodeReader.Decode(writeableBmp);
                if (result != null)
                {
                    var text = result.Text;
                    var servers = Utils.ShadowsocksUtils.GetServers(text);
                    if (servers.Count > 0)
                    {
                        await Utils.ShadowsocksUtils.SaveServersAsync(servers);
                        Frame.GoBack();
                    }
                }
            });
        }

        private async void InitVideoCapture()
        {
            var cameraDevice = await FindCameraDeviceByPanelAsync(Windows.Devices.Enumeration.Panel.Back);
            if (cameraDevice == null)
            {
                await new MessageDialog("No camera device found!").ShowAsync();
                return;
            }

            var settings = new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MediaCategory = MediaCategory.Other,
                AudioProcessing = Windows.Media.AudioProcessing.Default,
                PhotoCaptureSource = PhotoCaptureSource.VideoPreview,
                VideoDeviceId = cameraDevice.Id
            };
            try
            {
                await mediaCapture.InitializeAsync(settings);
            }
            catch (UnauthorizedAccessException)
            {
                await Utils.Utils.NotifyUser("Please turn on the camera permission of the app to ensure scan QR code normaly.");
                return;
            }

            var focusControl = mediaCapture.VideoDeviceController.FocusControl;
            if (focusControl.Supported)
            {
                var focusSettings = new FocusSettings()
                {
                    Mode = focusControl.SupportedFocusModes.FirstOrDefault(f => f == FocusMode.Continuous),
                    DisableDriverFallback = true,
                    AutoFocusRange = focusControl.SupportedFocusRanges.FirstOrDefault(f => f == AutoFocusRange.FullRange),
                    Distance = focusControl.SupportedFocusDistances.FirstOrDefault(f => f == ManualFocusDistance.Nearest)
                };
                focusControl.Configure(focusSettings);
            }
            VideoCapture.Source = mediaCapture;
            VideoCapture.FlowDirection = FlowDirection.LeftToRight;
            await mediaCapture.StartPreviewAsync();
            if (mediaCapture.VideoDeviceController.FlashControl.Supported)
            {
                mediaCapture.VideoDeviceController.FlashControl.Enabled = false;
            }

            if (focusControl.Supported)
            {
                await focusControl.FocusAsync();
            }
        }

        private static async Task<DeviceInformation> FindCameraDeviceByPanelAsync(Windows.Devices.Enumeration.Panel desiredPanel)
        {
            var allVideoDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            var desiredDevice = allVideoDevices.FirstOrDefault(x => x.EnclosureLocation != null && x.EnclosureLocation.Panel == desiredPanel);
            return desiredDevice ?? allVideoDevices.FirstOrDefault();
        }

        private async void FromPictureButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail,
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
                FileTypeFilter = { ".jpg", ".jpeg", ".png" }
            };
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                using (var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read))
                {
                    var writeableBmp = await ReadBitmap(stream, file.FileType);
                    await Task.Factory.StartNew(async () => { await ScanBitmap(writeableBmp); });
                }
            }
        }
    }
}