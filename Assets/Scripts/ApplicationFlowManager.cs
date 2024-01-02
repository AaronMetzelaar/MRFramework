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
    [NonSerialized] private ObjectInitiator objectInitiator;
    [NonSerialized] public ObjectDetector objectDetector;
    [SerializeField] private TextMeshProUGUI instructionText;
    [SerializeField] private RawImage canvasPreviewer;
    [SerializeField] private RawImage fullImage;
    private AppState currentState = AppState.Calibration;

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

        if (!TryGetComponent(out objectInitiator))
        {
            Debug.LogError("ObjectInitiator not found in the scene.");
        }
        // Set initial instruction text
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
                    currentState = AppState.Initiation;
                    calibrator.isCalibrating = false;
                    objectInitiator.webCamTexture = calibrator.webcamTexture;
                    objectInitiator.Initialize();
                    objectInitiator.isInitiating = true;
                    canvasPreviewer.enabled = false;
                    instructionText.text = "Place your object in the center of the canvas.\n\n" +
                                           "Press <b>Spacebar</b> to initiate object detection.\n" +
                                           "Press <b>Enter</b> when satisfied.";
                }
                break;
            case AppState.Initiation:
                if (Input.GetKeyDown(KeyCode.Return))
                {
                    currentState = AppState.Simulation;
                    objectInitiator.isInitiating = false;
                    instructionText.text = "Press <b>Spacebar</b> to initiate object detection.";
                }
                break;
            case AppState.Simulation:
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    instructionText.text = null;
                    objectDetector.StartDetecting();
                }
                break;
        }
    }
}