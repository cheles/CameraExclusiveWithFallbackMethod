using System;
using Windows.UI.Xaml.Controls;
using Windows.Media.Capture;
using Windows.Storage;
using Windows.Media.MediaProperties;
using Windows.UI.Xaml.Navigation;
using Windows.ApplicationModel;
using System.Threading.Tasks;
using Windows.System.Display;
using Windows.Graphics.Display;
using Windows.UI.Xaml;

namespace NewCamera
{
    public sealed partial class MainPage : Page
    {
        private MediaCapture _mediaCapture;
        private bool _isPreviewing;
        private readonly DisplayRequest _displayRequest = new DisplayRequest();

        public MainPage()
        {
            this.InitializeComponent();
            Application.Current.Suspending += Application_Suspending;
        }

        private async Task InitializeCameraAsync(MediaCaptureSharingMode sharingMode)
        {
            if (_mediaCapture == null)
            {
                // Create MediaCapture and its settings
                _mediaCapture = new MediaCapture();

                // Subscribe to the Failed event for error handling
                _mediaCapture.Failed += MediaCapture_Failed;

                try
                {
                    // Initialize MediaCapture with the specified sharing mode
                    await _mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
                    {
                        SharingMode = sharingMode, // Set the sharing mode
                        StreamingCaptureMode = StreamingCaptureMode.AudioAndVideo,
                        MediaCategory = MediaCategory.Other
                    });
                    _isPreviewing = true;
                    // Update the TextBlock with the current sharing mode
                    SharingModeTextBlock.Text = $"Sharing Mode: {sharingMode}";
                }
                catch (UnauthorizedAccessException)
                {
                    // This will be thrown if the user denied access to the camera in privacy settings
                    System.Diagnostics.Debug.WriteLine("The app was denied access to the camera");
                    return;
                }
                catch (Exception ex)
                {
                     System.Diagnostics.Debug.WriteLine($"Exception when initializing MediaCapture: {ex.Message}");
                    // Fallback to SharedReadOnly mode if ExclusiveControl fails
                    if (sharingMode == MediaCaptureSharingMode.ExclusiveControl)
                    {
                        await InitializeCameraAsync(MediaCaptureSharingMode.SharedReadOnly);
                    }
                    return;
                }
            }

            // If initialization succeeded, start the preview
            PreviewControl.Source = _mediaCapture; // Assign the MediaCapture to the CaptureElement
            await _mediaCapture.StartPreviewAsync();
            // Update the TextBlock if not already set (e.g. if _mediaCapture was not null)
            if (SharingModeTextBlock.Text == "Initializing...")
            {
                SharingModeTextBlock.Text = $"Sharing Mode: {sharingMode}";
            }
            _displayRequest.RequestActive(); // Keep display active
            DisplayInformation.AutoRotationPreferences = DisplayOrientations.Landscape; // Set orientation
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            await InitializeCameraAsync(MediaCaptureSharingMode.ExclusiveControl); // Initialize with ExclusiveControl first
        }

        private async void MediaCapture_Failed(MediaCapture sender, MediaCaptureFailedEventArgs errorEventArgs)
        {
            System.Diagnostics.Debug.WriteLine($"MediaCapture_Failed: (0x{errorEventArgs.Code:X}) {errorEventArgs.Message}");
            await CleanupCameraAsync();
            // Try to reinitialize with SharedReadOnly mode upon failure
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                await InitializeCameraAsync(MediaCaptureSharingMode.SharedReadOnly);
            });
        }

        private async void PhotoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Prepare for low lag photo capture
                var lowLagCapture = await _mediaCapture.PrepareLowLagPhotoCaptureAsync(ImageEncodingProperties.CreateUncompressed(MediaPixelFormat.Bgra8));

                // Take the photo
                var capturedPhoto = await lowLagCapture.CaptureAsync();

                // Release the LowLagPhotoCapture session
                await lowLagCapture.FinishAsync();

                // Save the photo to the Pictures library
                var myPictures = await StorageLibrary.GetLibraryAsync(KnownLibraryId.Pictures);
                StorageFile file = await myPictures.SaveFolder.CreateFileAsync("photo.jpg", CreationCollisionOption.GenerateUniqueName);

                using (var captureStream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                {
                    // Recapture the photo to a stream for saving with correct encoding
                    await _mediaCapture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), captureStream);

                    using (var fileStream = await file.OpenAsync(FileAccessMode.ReadWrite))
                    {
                        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(captureStream);
                        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateForTranscodingAsync(fileStream, decoder);

                        // Ensure the photo orientation is correct (optional, adjust as needed)
                        var properties = new Windows.Graphics.Imaging.BitmapPropertySet { { "System.Photo.Orientation", new Windows.Graphics.Imaging.BitmapTypedValue(1, Windows.Foundation.PropertyType.UInt16) } };
                        await encoder.BitmapProperties.SetPropertiesAsync(properties);

                        await encoder.FlushAsync();
                    }
                }
                 System.Diagnostics.Debug.WriteLine("Photo saved to " + file.Path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception when taking a photo: {ex.Message}");
            }
        }
        private async Task CleanupCameraAsync()
        {
            if (_mediaCapture != null)
            {
                if (_isPreviewing)
                {
                    try
                    {
                        await _mediaCapture.StopPreviewAsync();
                        _isPreviewing = false;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Exception when stopping preview: {ex.Message}");
                    }

                }
                // Release the media capture resources
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    PreviewControl.Source = null;
                    if (_displayRequest != null)
                    {
                        _displayRequest.RequestRelease(); // Release display request
                    }
                    _mediaCapture.Dispose();
                    _mediaCapture = null;
                });
            }
        }

        private async void Application_Suspending(object sender, SuspendingEventArgs e)
        {
            // Handle global application events only if this page is active
            if (Frame.CurrentSourcePageType == typeof(MainPage))
            {
                var deferral = e.SuspendingOperation.GetDeferral();
                await CleanupCameraAsync();
                deferral.Complete();
            }
        }

        protected override async void OnNavigatedFrom(NavigationEventArgs e)
        {
            await CleanupCameraAsync(); // Clean up resources when navigating away
        }
    }
}

