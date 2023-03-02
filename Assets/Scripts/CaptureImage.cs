using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class CaptureImage : MonoBehaviour
{
    public const int RenderTextureDepth = 24;
    private const int MaxTrackedImages = 3;
    private const float ImagePhysicalWidth = 0.1f;

    [SerializeField] private Button takePhotoButton;
    [SerializeField] private Transform photosContent;
    [SerializeField] private GameObject photoPrefab;
    [SerializeField] private GameObject prefabToInstantiate;

    private Dictionary<string, GameObject> instantiatedPrefabs;
    private ARTrackedImageManager arTrackedImageManager;
    private Camera mainCamera;
    private GameObject recognizedImageObject;

    private void Awake()
    {
        mainCamera = Camera.main;
        arTrackedImageManager = gameObject.AddComponent<ARTrackedImageManager>();
        instantiatedPrefabs = new Dictionary<string, GameObject>();
        takePhotoButton.interactable = false;
    }

    private void OnEnable()
    {
        takePhotoButton.onClick.AddListener(TakePhoto);
    }

    private void OnDisable()
    {
        takePhotoButton.onClick.RemoveListener(TakePhoto);
    }

    private void Start()
    {
        if (Application.platform != RuntimePlatform.WindowsEditor)
        {
            arTrackedImageManager.referenceLibrary = arTrackedImageManager.CreateRuntimeLibrary();
            arTrackedImageManager.requestedMaxNumberOfMovingImages = MaxTrackedImages;
            StartCoroutine(WaitForStartTracking());
        }
    }

    private IEnumerator WaitForStartTracking()
    {
        yield return new WaitUntil(() => ARSession.state == ARSessionState.SessionTracking);
        StartTracker();
        takePhotoButton.interactable = true;
        print("Can take photo!");
    }

    private void StartTracker()
    {
        print("Ready to track images!");
        arTrackedImageManager.enabled = true;
        arTrackedImageManager.trackedImagesChanged += ArTrackedImageManager_trackedImagesChanged;
    }

    private void TakePhoto()
    {
        if (takePhotoButton.interactable)
        {
            StartCoroutine(TakePhotoCoroutine());
        }
    }

    private IEnumerator TakePhotoCoroutine()
    {
        takePhotoButton.interactable = false;
        yield return StartCoroutine(CaptureImageCoroutine());
        takePhotoButton.interactable = true;
    }

    private IEnumerator CaptureImageCoroutine()
    {
        yield return new WaitForEndOfFrame();
        print("Taking photo...");

        var screenshot = GetScreenshotWithoutUI();
        var photoUI = Instantiate(photoPrefab, photosContent);
        photoUI.GetComponent<SetImage>().SetPhoto(screenshot);

        yield return StartCoroutine(AddImage(screenshot));
    }

    private Texture2D GetScreenshotWithoutUI()
    {
        var currentRenderTexture = RenderTexture.active;
        var renderTexture = new RenderTexture(Screen.width, Screen.height, RenderTextureDepth);
        mainCamera.targetTexture = renderTexture;
        RenderTexture.active = renderTexture;
        mainCamera.Render();

        var image = new Texture2D(mainCamera.targetTexture.width, mainCamera.targetTexture.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, mainCamera.targetTexture.width, mainCamera.targetTexture.height), 0, 0);
        image.Apply();
        image.name = Guid.NewGuid().ToString();

        CleanupForPhotoCapturing(currentRenderTexture, renderTexture);
        return image;
    }

    private void CleanupForPhotoCapturing(RenderTexture currentRenderTexture, RenderTexture renderTexture)
    {
        mainCamera.targetTexture = null;
        RenderTexture.active = currentRenderTexture;
        Destroy(renderTexture);
        Destroy(currentRenderTexture);
    }

    private void ArTrackedImageManager_trackedImagesChanged(ARTrackedImagesChangedEventArgs obj)
    {
        foreach (var trackedImage in obj.added)
        {
            print($"trackedImage name: {trackedImage.referenceImage.name}");
            var imageObject = Instantiate(prefabToInstantiate, trackedImage.transform.position, Quaternion.identity);
            instantiatedPrefabs.Add(trackedImage.referenceImage.name, imageObject);
            TrackImage(trackedImage);
        }
        foreach (var trackedImage in obj.updated)
        {
            TrackImage(trackedImage);
        }
        foreach (var trackedImage in obj.removed)
        {
            TrackImage(trackedImage);
        }
    }

    private void TrackImage(ARTrackedImage trackedImage)
    {
        recognizedImageObject = instantiatedPrefabs[trackedImage.referenceImage.name];
        if (trackedImage.trackingState == TrackingState.Tracking)
        {
            //print($"tracking: {recognizedImageObject.transform.position} is now at: {trackedImage.transform.position}");
            recognizedImageObject.transform.position = trackedImage.transform.position;
            recognizedImageObject.transform.rotation = trackedImage.transform.rotation;
            recognizedImageObject.SetActive(true);
        }
        else
        {
            print("tracking lost");
            recognizedImageObject.SetActive(false);
        }
    }

    private IEnumerator AddImage(Texture2D texture2D)
    {
        print("Add image to library...");
        var formattedImage = ChangeFormat(texture2D, TextureFormat.R8);
        // Destroy(texture2D); - we use it in the UI, so it shouldn't be destroyed. Only if you don't want them present in UI, this is needed for memory cleanup.
        yield return StartCoroutine(AddImage(new NativeArray<byte>(formattedImage.GetRawTextureData<byte>(), Allocator.Persistent), formattedImage.format, formattedImage.width, formattedImage.height, ImagePhysicalWidth, texture2D.name));
        Destroy(formattedImage);
    }

    private IEnumerator AddImage(NativeArray<byte> grayscaleImageBytes, TextureFormat format,
              int widthInPixels,
              int heightInPixels,
              float widthInMeters,
              string imageName)
    {
        if (arTrackedImageManager.referenceLibrary is MutableRuntimeReferenceImageLibrary mutableLibrary)
        {
            var aspectRatio = (float)heightInPixels / (float)widthInPixels;
            var sizeInMeters = new Vector2(widthInMeters, widthInMeters * aspectRatio);
            var referenceImage = new XRReferenceImage(
                SerializableGuid.empty,
                SerializableGuid.empty,
                sizeInMeters, imageName, null);

            var jobState = mutableLibrary.ScheduleAddImageWithValidationJob(
                grayscaleImageBytes,
                new Vector2Int(widthInPixels, heightInPixels),
                format,
                referenceImage);

            new DeallocateJob { data = grayscaleImageBytes }.Schedule(jobState.jobHandle);
            yield return new WaitUntil(() => jobState.jobHandle.IsCompleted);
            print("Image added in the library.");
        }
        else
        {
            grayscaleImageBytes.Dispose();
        }
    }

    private Texture2D ChangeFormat(Texture2D oldTexture, TextureFormat newFormat)
    {
        Texture2D newTex = new Texture2D(oldTexture.width, oldTexture.height, newFormat, false);
        newTex.SetPixels(oldTexture.GetPixels());
        newTex.Apply();

        return newTex;
    }

    private struct DeallocateJob : IJob
    {
        [DeallocateOnJobCompletion]
        public NativeArray<byte> data;
        public void Execute() { }
    }
}
