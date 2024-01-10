using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ApplicationFlowManager : MonoBehaviour
{
    enum AppState
    {
        Calibration,
        Initiation,
        Simulation
    }

    [NonSerialized] private Calibrator calibrator;
    [NonSerialized] private ObjectInitializer objectInitializer;
    [NonSerialized] public ObjectDetector objectDetector;
    [SerializeField] private ObjectData objectData;
    [SerializeField] private TextMeshProUGUI instructionText;
    [SerializeField] private RawImage canvasPreviewer;
    [SerializeField] private RawImage fullImage;
    [Tooltip("Enable this to save the initialized object data between sessions, skipping the initiation step.")]
    [SerializeField] public bool saveObjectData = false;
    private AppState currentState = AppState.Calibration;
    private bool firstInitiation = true;

    /// <summary>
    /// This method is called when the script instance is being loaded.
    /// It initializes the calibrator, sets the initial instruction text,
    /// and checks for the presence of required components.
    /// </summary>
    void Start()
    {
        calibrator = GetComponent<Calibrator>();
        fullImage.gameObject.SetActive(false);
        if (calibrator == null)
        {
            Debug.LogError("Calibrator not found in the scene.");
        }

        if (!TryGetComponent(out objectInitializer))
        {
            Debug.LogError("ObjectInitializer not found in the scene.");
        }

        if (!TryGetComponent(out objectDetector))
        {
            Debug.LogError("ObjectDetector not found in the scene.");
        }

        instructionText.text = "If the border is incorrect, make sure the entire playing field is visible.\n" +
                               "Also, make sure there is enough contrast between the playing field\n" +
                               "and the surface underneath.\n\n" +
                               "Press <b>Spacebar</b> to recalibrate.\n" +
                               "Press <b>Enter</b> to continue.";
    }

    /// <summary>
    /// This method is called once per frame and handles the update logic for the application flow.
    /// </summary>
    void Update()
    {
        switch (currentState)
        {
            case AppState.Calibration:
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    StartCoroutine(calibrator.Recalibrate());
                }
                if (Input.GetKeyDown(KeyCode.Return))
                {
                    calibrator.isCalibrating = false;
                    objectInitializer.webCamTexture = calibrator.webcamTexture;
                    canvasPreviewer.enabled = false;

                    if (saveObjectData && objectData.objectDataList.Count > 0)
                    {
                        currentState = AppState.Simulation;
                        instructionText.text = "Press <b>Spacebar</b> to initiate object detection.\n";
                    }
                    else
                    {
                        currentState = AppState.Initiation;
                        objectInitializer.Initialize();
                        instructionText.text = "Place your object in the center of the canvas.\n\n" +
                                               "Press <b>Spacebar</b> to initiate object detection.\n";
                    }
                }
                break;
            case AppState.Initiation:
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    if (firstInitiation)
                    {
                        firstInitiation = false;
                        StartCoroutine(objectInitializer.DelayedInitiate());
                        instructionText.text = "To change the color of the object, go to ApplicationManager > Object Initializer > Object Color.\n\n" +
                                               "Press <b>Spacebar</b> to reinitialize.\n" +
                                               "Press <b>Enter</b> to save the object and continue.";
                    }
                    else
                    {
                        objectInitializer.Reinitiate();
                    }
                }
                if (Input.GetKeyDown(KeyCode.Return))
                {
                    objectInitializer.SaveObjectToList();
                    Destroy(objectInitializer.currentVisualizedObject);
                    fullImage.texture = null;
                    currentState = AppState.Simulation;
                    instructionText.text = "Press <b>Spacebar</b> to initiate object detection.";
                }
                break;
            case AppState.Simulation:
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    objectDetector.webCamTexture = calibrator.webcamTexture;
                    instructionText.text = null;
                    objectDetector.Initialize();
                    objectDetector.StartDetecting();
                }
                if (Input.GetKeyDown(KeyCode.Return))
                {
                    objectDetector.StopDetecting();
                }
                break;
        }
    }

    void OnApplicationQuit()
    {
        if (!saveObjectData)
            objectData.ClearData();
    }
}