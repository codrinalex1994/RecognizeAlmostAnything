using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class TrackImages : MonoBehaviour
{
    private const int MaxTrackedImages = 3;
    private const float ImagePhysicalWidth = 0.1f;

    [SerializeField] private List<GameObject> prefabToInstantiate;
    [SerializeField] private CaptureImage captureImage;
    [SerializeField] private Button anchorObjectButton;

    private List<GameObject> anchoredObjects;
    private Dictionary<string, GameObject> instantiatedPrefabs;
    private ARTrackedImageManager arTrackedImageManager;
    private GameObject recognizedImageObject;
    private int prefabIndex = 0;

    private void Awake()
    {
        anchoredObjects = new List<GameObject>();
        arTrackedImageManager = gameObject.AddComponent<ARTrackedImageManager>();
        instantiatedPrefabs = new Dictionary<string, GameObject>();
        captureImage.PhotoCaptured.AddListener(OnPhotoCaptured);
        anchorObjectButton.onClick.AddListener(OnAnchorObjectPressed);
        anchorObjectButton.interactable = false;
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
        print("StartTracker!");
        StartTracker();
    }

    private void StartTracker()
    {
        arTrackedImageManager.enabled = true;
        arTrackedImageManager.trackedImagesChanged += ArTrackedImageManager_trackedImagesChanged;
    }

    private void OnPhotoCaptured(Texture2D texture2D)
    {
        StartCoroutine(AddImage(texture2D));
    }

    private void OnAnchorObjectPressed()
    {
        if (recognizedImageObject != null)
        {
            recognizedImageObject.transform.SetParent(transform);
            recognizedImageObject.SetActive(true);
            anchoredObjects.Add(recognizedImageObject);
            recognizedImageObject = null;
        }
    }

    private void ArTrackedImageManager_trackedImagesChanged(ARTrackedImagesChangedEventArgs obj)
    {
        print("ArTrackedImageManager_trackedImagesChanged");
        foreach (var trackedImage in obj.added)
        {
            print("track image: " + trackedImage.name);
            if (!instantiatedPrefabs.ContainsKey(trackedImage.referenceImage.name))
            {
                var imageObject = Instantiate(prefabToInstantiate[prefabIndex], trackedImage.transform.position, Quaternion.identity);
                prefabIndex = (prefabIndex + 1) % prefabToInstantiate.Count;
                instantiatedPrefabs.Add(trackedImage.referenceImage.name, imageObject);
                print("Instantiate prefab: " + imageObject.name);
            }
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
        if (recognizedImageObject != null && !anchoredObjects.Contains(recognizedImageObject))
        {
            if (trackedImage.trackingState == TrackingState.Tracking)
            {
                recognizedImageObject.transform.position = trackedImage.transform.position;
                recognizedImageObject.transform.rotation = trackedImage.transform.rotation;
                recognizedImageObject.SetActive(true);
                anchorObjectButton.interactable = true;
                print("Set track image object position!");
            }
            else
            {
                recognizedImageObject.SetActive(false);
                anchorObjectButton.interactable = false;
            }
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
