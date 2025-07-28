using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KModkit;
using UnityEngine;

public class FriedEgg : MonoBehaviour {

    public KMBombModule module;
    public KMSelectable knob;
    public KMSelectable egg;
    public GameObject crackedEgg;
    public TextMesh timerText;

    public int secondsLeft = 120;

    private bool isActivated = false;
    private int currentHeat = 0; // 0: Off, 1: Low, 2: Medium, 3: High

    [SerializeField]
    private Color[] baseColorList = new Color[4]; // White, Buff, Green, Gray
    [SerializeField]
    private Color[] accentColorList = new Color[4]; // Green, Gray, Blue, Magenta
    [SerializeField]
    private Texture2D dottedMask;
    [SerializeField]
    private Texture2D stripedMask;

    private int baseColorIndex;
    private int accentColorIndex;
    private bool isDotted; // Is the egg dotted or striped
    private bool isAlive; // Used for the shaking rule

    private KMBombInfo bombInfo;

    private int correctHeat;
    private int perfectTime;
    private bool isCooking = false;
    private Coroutine timerCoroutine;
    private bool isSolved = false;
    private float tickRate = 1f; // How many seconds between a second is subtracted from the timer

    // Logging
    static int moduleIdCounter = 1;
    int moduleId;

    private Animator anim;

    // Use this for initialization
    void Start () {
        moduleId = moduleIdCounter++;
        bombInfo = GetComponent<KMBombInfo>();
		module = GetComponent<KMBombModule>();

        baseColorIndex = Random.Range(0, baseColorList.Length);
        accentColorIndex = Random.Range(0, accentColorList.Length);
        isDotted = Random.Range(0, 2) == 0;
        isAlive = Random.Range(0, 9) == 0; // 10% chance to be alive
        // disable the animator
        anim = GetComponent<Animator>();
        if (anim != null)
        {
            anim.enabled = false;
        }

        // Set the egg's visual appearance based on colors and pattern
        var eggRenderer = egg.GetComponent<Renderer>();
        if (eggRenderer != null)
        {
            var mat = eggRenderer.material;
            mat.SetColor("_BaseColor", baseColorList[baseColorIndex]);
            mat.SetColor("_AccentColor", accentColorList[accentColorIndex]);
            mat.SetTexture("_MaskTex", isDotted ? dottedMask : stripedMask);
        }
        else
        {
            Log("Could not find Renderer on the egg object to set its appearance.");
        }

        module.OnActivate += ActivateModule;
        knob.OnInteract += OnKnobPress;
        egg.OnInteract += OnEggPress;

        // Pre-calculate the solution
        CalculateSolution();
    }

    void ActivateModule()
    {
        isActivated = true;
        secondsLeft = CalculateTotalTimer();
    }

    int CalculateTotalTimer(){
        int totalTime = 0;
        // total len / module count
        float timeForModule = bombInfo.GetTime() / bombInfo.GetModuleNames().Count;
        Log(bombInfo.GetTime() + "/" + bombInfo.GetModuleNames().Count + " = " + timeForModule + " seconds per module");

        // set the timer to a random time between 50% and 150% of the average module time, but not exceeding 95% of the total bomb time
        totalTime = Mathf.RoundToInt(Mathf.Clamp(timeForModule * Random.Range(0.5f, 1.5f), 0, bombInfo.GetTime() * 0.95f));
        // Cap at 8 minutes
        if(totalTime > 480){
            totalTime = Random.Range(460, 500);
        }
        return totalTime;
    }

    void CalculateSolution()
    {
        // 1. Determine egg name parts
        string[] baseNames = { "Ovolumen", "Fulvovum", "Herbovum", "Cinereovum" };
        string[] accentNamesStriped = { "viristria", "grisestria", "caerulestria", "purpurestria" };
        string[] accentNamesDotted = { "viripuncta", "grisepuncta", "caerulepuncta", "purpurepuncta" };

        string baseName = baseNames[baseColorIndex];
        string accentName = isDotted ? accentNamesDotted[accentColorIndex] : accentNamesStriped[accentColorIndex];
        string eggName = baseName + " " + accentName;
        Log("Egg is: " + eggName);

        // 2. Calculate correct heat
        int heat = 0;
        if (isAlive) heat = 3; // shaking special rule
        else if (baseName == "Cinereovum" && bombInfo.GetBatteryCount() >= 2) heat = 2;
        else if (accentName == "grisestria") heat = bombInfo.GetSerialNumberNumbers().Last();
        else if (baseName == "Ovolumen" && bombInfo.IsIndicatorOn(Indicator.SIG)) heat = bombInfo.GetPortCount();
        else if (baseName == "Fulvovum" && bombInfo.GetBatteryHolderCount() == 3) heat = 3;
        else if (accentName == "viripuncta") heat = bombInfo.GetOffIndicators().Count();
        else if (baseName == "Herbovum" && bombInfo.GetModuleNames().Count() > bombInfo.GetPortCount()) heat = 1;
        else if (accentName == "caerulepuncta") heat = bombInfo.GetModuleNames().Count();
        else if (baseName == "Fulvovum" && bombInfo.GetOnIndicators().Count() == 0) heat = 1;
        else if (accentName == "purpurestria") heat = 3;
        else if (accentName == "viristria") heat = bombInfo.GetBatteryHolderCount();
        else heat = 2;

        // Normalize heat to be in range 1-3
        if (heat < 1) correctHeat = ((heat - 1) % 3 + 3) % 3 + 1;
        else correctHeat = (heat - 1) % 3 + 1;
        
        Log("Calculated heat: " + heat + ", Correct heat: " + correctHeat);

        // 3. Calculate timer offset
        int offset = 0;
        int firstSerialDigit = bombInfo.GetSerialNumberNumbers().First();
        if (firstSerialDigit % 2 != 0) // Odd
        {
            offset = bombInfo.GetBatteryHolderCount() - (correctHeat * 4);
        }
        else // Even
        {
            offset = (correctHeat * 3) + bombInfo.GetPortCount() - bombInfo.GetOffIndicators().Count();
        }

        perfectTime = -offset;
        Log("Timer offset: " + offset + ", Perfect time: " + perfectTime);
    }

    string TimeToString(float time){
        int minutes = (int)(time / 60);
        int seconds = Mathf.Abs((int)(time % 60));
        if (time < 0)
        {
            return string.Format("-{0:00}:{1:00}", minutes, seconds);
        }
        return string.Format("{0:00}:{1:00}", minutes, seconds);
    }

    IEnumerator StartTimer(){
        while (true){
            timerText.text = TimeToString(secondsLeft);
            if (isCooking)
            {
                if (secondsLeft < perfectTime - 5)
                {
                    Log("Cooked for too long. Egg is burnt. Module auto-solving.");
                    isSolved = true;
                    GetComponent<KMBombModule>().HandlePass();
                    timerText.text = "burnt";
                    isCooking = false;
                    yield break;
                }
            }
            yield return new WaitForSeconds(tickRate);
            // if this module is the last one, increase tickRate to 0.25
            if (bombInfo.GetModuleNames().Count == 1)
            {
                tickRate = 0.25f;
            }
            secondsLeft--;
        }
    }

    bool OnEggPress(){
        if (!isActivated || isSolved || isCooking) return false;
        
        GetComponent<KMSelectable>().AddInteractionPunch();
        if (currentHeat == correctHeat)
        {
            Log("Egg placed on pan with correct heat. Cooking started.");
            isCooking = true;
            egg.gameObject.SetActive(false);
            crackedEgg.SetActive(true);
            crackedEgg.transform.localRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
            // if the cooking is started too late, be kind and make the timer shorter
            if (bombInfo.GetTime() < secondsLeft + 5){
                secondsLeft = CalculateTotalTimer();
            }
            timerCoroutine = StartCoroutine(StartTimer());
        }
        else
        {
            Log("Egg placed on pan with incorrect heat (" + currentHeat + "). Strike!");
            GetComponent<KMBombModule>().HandleStrike();
        }
        return false;
    }

    bool OnKnobPress(){
        if (!isActivated || isSolved) return false;
        
        GetComponent<KMSelectable>().AddInteractionPunch();

        if(isAlive & !anim.enabled){
            anim.enabled = true;
        }

        if (isCooking)
        {
            // Turning knob while cooking means turning it off
            if (secondsLeft >= perfectTime - 5 && secondsLeft <= perfectTime + 5)
            {
                Log("Heat turned off at the correct time. Module solved!");
                isSolved = true;
                GetComponent<KMBombModule>().HandlePass();
                if (timerCoroutine != null) StopCoroutine(timerCoroutine);
                timerText.text = "egg";
            	currentHeat = 0;
            	isCooking = false; // Stop cooking checks
            }
            else
            {
                Log("Heat turned off at the wrong time (" + secondsLeft + "). Strike!");
                GetComponent<KMBombModule>().HandleStrike();
            }
        }
        else
        {
            currentHeat++;
            currentHeat %= 4; // Cycle through heat levels 0-3
        }

        knob.transform.localRotation = Quaternion.Euler(0, -45 + (currentHeat * 30), 0);
        return false;
    }
    
    // Update is called once per frame
    void Update () {
        
    }

    private void Log(string message)
    {
        Debug.LogFormat("[Fried Egg #{0}] {1}", moduleId, message);
    }
}
