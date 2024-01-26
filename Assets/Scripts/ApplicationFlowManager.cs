using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ApplicationFlowManager : MonoBehaviour
{
    enum AppState
    {
        Calibration,
        Initialization,
        Application
    }

    [NonSerialized]
    private Calibrator calibrator;

    [NonSerialized]
    private ObjectInitializer objectInitializer;

    [NonSerialized]
    private ObjectDetector objectDetector;

    [Tooltip(
        "The application file to load. This file should have a list of objects to be initialized."
    )]
    [NonSerialized]
    private RGBDemoApplication simulation;

    [SerializeField]
    private ObjectData objectData;

    [SerializeField]
    private TextMeshProUGUI instructionText;

    [SerializeField]
    private RawImage canvasPreviewImage;

    [SerializeField]
    private RawImage fullImage;

    [Tooltip(
        "Enable this to save the initialized object data between sessions, skipping the initiation step."
    )]
    [SerializeField]
    public bool saveObjectData = false;
    private AppState currentState = AppState.Calibration;
    private bool firstInitialization = true;
    private int objectsToInitializeIndex = 0;

    /// <summary>
    /// This method is called when the script instance is being loaded.
    /// It initializes the calibrator, sets the initial instruction text,
    /// and checks for the presence of required components.
    /// </summary>
    void Start()
    {
        if (!TryGetComponent(out calibrator))
            Debug.LogError("Calibrator not found in the scene.");

        if (!TryGetComponent(out objectInitializer))
            Debug.LogError("ObjectInitializer not found in the scene.");

        if (!TryGetComponent(out objectDetector))
            Debug.LogError("ObjectDetector not found in the scene.");

        if (!TryGetComponent(out simulation))
            Debug.LogError("Simulation not found in the scene.");

        instructionText.text =
            "If the border is incorrect, make sure the entire playing field is visible.\n"
            + "Also, make sure there is enough contrast between the playing field\n"
            + "and the surface underneath.\n\n"
            + "Press <b>Spacebar</b> to recalibrate.\n"
            + "Press <b>B</b> to set a base image based on the current found rectangle.\n"
            + "Press <b>Enter</b> to continue.";
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
                    StartCoroutine(calibrator.Recalibrate());
                if (Input.GetKeyDown(KeyCode.B))
                    calibrator.SetBaseImage();
                if (Input.GetKeyDown(KeyCode.Return))
                {
                    calibrator.isCalibrating = false;
                    objectInitializer.webCamTexture = calibrator.webcamTexture;
                    canvasPreviewImage.enabled = false;

                    if (saveObjectData && objectData.objectDataList.Count > 0)
                    {
                        currentState = AppState.Application;
                        instructionText.text =
                            "Press <b>Spacebar</b> to initiate object detection.\n";
                    }
                    else
                    {
                        currentState = AppState.Initialization;
                        objectInitializer.Initialize();
                        objectInitializer.InitializeNamedObject(
                            simulation.objectsToInitialize[0].Item1,
                            simulation.objectsToInitialize[0].Item2,
                            simulation.objectsToInitialize[0].Item3
                        );
                        instructionText.text =
                            $"Place the <b>{simulation.objectsToInitialize[0].Item1}</b> object in the center of the canvas.\n\n"
                            + "Press <b>Spacebar</b> to initiate object detection.\n";
                    }
                }
                break;
            case AppState.Initialization:
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    if (firstInitialization)
                    {
                        firstInitialization = false;
                        StartCoroutine(objectInitializer.DelayedIntialize());
                        instructionText.text =
                            "To change the color of the object, go to ApplicationManager > Object Initializer > Object Color.\n\n"
                            + "Press <b>Spacebar</b> to reinitialize.\n"
                            + "Press <b>Enter</b> to save the object and continue.";
                    }
                    else
                    {
                        objectInitializer.Reinitialize();
                    }
                }
                if (
                    Input.GetKeyDown(KeyCode.Return)
                    && objectInitializer.currentVisualizedObject != null
                )
                {
                    objectsToInitializeIndex++;
                    if (objectInitializer.currentVisualizedObject != null)
                    {
                        objectInitializer.SaveObjectToList();
                        Destroy(objectInitializer.currentVisualizedObject);
                    }

                    if (simulation.objectsToInitialize.Count - 1 >= objectsToInitializeIndex)
                    {
                        objectInitializer.InitializeNamedObject(
                            simulation.objectsToInitialize[objectsToInitializeIndex].Item1,
                            simulation.objectsToInitialize[objectsToInitializeIndex].Item2,
                            simulation.objectsToInitialize[objectsToInitializeIndex].Item3
                        );
                        instructionText.text =
                            $"Place the <b>{simulation.objectsToInitialize[objectsToInitializeIndex].Item1}</b> object in the center of the canvas.\n\n"
                            + "Press <b>Spacebar</b> to reinitialize.\n"
                            + "Press <b>Enter</b> to save the object and continue.";
                    }
                    else
                    {
                        currentState = AppState.Application;
                        instructionText.text =
                            "Press <b>Spacebar</b> to initiate object detection.";
                    }
                }
                break;
            case AppState.Application:
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    objectDetector.webCamTexture = calibrator.webcamTexture;
                    instructionText.text = null;
                    objectDetector.Initialize();
                    simulation.Initialize();
                    objectDetector.StartDetecting();
                }
                if (Input.GetKeyDown(KeyCode.Return))
                    objectDetector.StopDetecting();
                break;
        }
    }

    void OnApplicationQuit()
    {
        if (!saveObjectData)
            objectData.ClearData();
    }
}
