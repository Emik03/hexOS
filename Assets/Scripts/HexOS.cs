using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Video;
using Rnd = UnityEngine.Random;

public class HexOS : MonoBehaviour
{
    public class ModSettingsJSON
    {
        public bool disableOctOS, fastStrike, experimentalShake, forceAltSolve;
        public byte flashOtherColors;
        public float delayPerBeat;
        public string customSolveQuote;
    }

    public KMAudio Audio;
    public KMBombInfo Info;
    public KMBombModule Module;
    public KMModSettings ModSettings;
    public KMSelectable Button;
    public MeshRenderer[] Ciphers, Cylinders;
    public Renderer Background, Foreground, VideoRenderer;
    public TextMesh Number, UserNumber, Status, Quote, ModelName, GroupCounter;
    public Texture[] FrequencyTextures;
    public Transform[] Spinnables;
    public VideoPlayer VideoOct, VideoGrid;
    public VideoClip[] Clips;

    bool isSolved = false;
    char[] decipher = new char[2];
    string sum = "", screen = "";

    private static bool _forceAltSolve, _experimentalShake, _canBeOctOS;
    private bool _lightsOn, _octOS, _isHolding, _playSequence, _hasPlayedSequence, _octAnimating, _fastStrike;
    private static byte _flashOtherColors = 5;
    private sbyte _press = -1, _held = 0;
    private readonly char[] _tempDecipher = new char[2];
    private readonly byte[] _rhythms = new byte[2], _ciphers = new byte[6], _octRhythms = new byte[2], _octSymbols = new byte[18];
    private readonly List<byte> _octColors = new List<byte>(0);
    private static int _moduleIdCounter = 1, _y = 0, _rotationSpeed;
    private int _moduleId = 0;
    private static float _delayPerBeat, _hexOSStrikes;
    private static string _customSolveQuote;
    private string _user = "", _answer = "", _octAnswer = "", _submit = "", _tempScreen;

    /// <summary>
    /// Waits until the module has gathered the video clips before proceeding with generation.
    /// </summary>
    /// <returns>Waits until clips are gathered and the lights are on.</returns>
    private IEnumerator WaitForVideoClips()
    {
        if (!Application.isEditor)
            yield return new WaitUntil(() => VideoLoader.clips != null);
        Activate();
    }

    /// <summary>
    /// ModuleID and JSON Initialisation.
    /// </summary>
    private void Start()
    {
        // Give each module of hexOS a different number.
        _moduleId = _moduleIdCounter++;

        // Set the variables in case if they don't get set by ModSettings.
        _canBeOctOS = true;
        _fastStrike = false;
        _experimentalShake = false;
        _forceAltSolve = false;
        _flashOtherColors = 5;
        _delayPerBeat = 0.07f;
        _customSolveQuote = "";
        _hexOSStrikes = 0;

        // Get JSON settings.
        try
        {
            // Get settings.
            ModSettingsJSON settings = JsonConvert.DeserializeObject<ModSettingsJSON>(ModSettings.Settings);

            // If it contains information.
            if (settings != null)
            {
                // Get variables from mod settings.
                _canBeOctOS = !settings.disableOctOS;
                _fastStrike = settings.fastStrike;
                _experimentalShake = settings.experimentalShake;
                _forceAltSolve = settings.forceAltSolve;
                _flashOtherColors = Math.Min(settings.flashOtherColors, (byte)6);
                _delayPerBeat = Math.Min(Math.Abs(settings.delayPerBeat), 1);
                _customSolveQuote = settings.customSolveQuote;
            }
        }
        catch (JsonReaderException e)
        {
            // In the case of catastrophic failure and devastation.
            Debug.LogFormat("[hexOS #{0}] JSON reading failed with error: \"{1}\", resorting to default values.", _moduleId, e.Message);
        }

        // Hides ciphers
        for (byte i = 0; i < Ciphers.Length; i++)
            Ciphers[i].transform.localPosition = new Vector3(Ciphers[i].transform.localPosition.x, -2.1f, Ciphers[i].transform.localPosition.z);

        // Hide toilet if neither forget the colors or force alt solve is on, otherwise hide stars.
        _rotationSpeed = (byte)((Convert.ToByte(_forceAltSolve || Info.GetSolvableModuleNames().Contains("Forget The Colors")) * 9) + 1);
        byte hideIndex = (byte)(1 - Convert.ToByte(_forceAltSolve || Info.GetSolvableModuleNames().Contains("Forget The Colors")));
        for (byte i = (byte)(2 * hideIndex); i < 2 + hideIndex; i++)
            Spinnables[i].transform.localPosition = new Vector3(Spinnables[i].transform.localPosition.x, Spinnables[i].transform.localPosition.y / 3, Spinnables[i].transform.localPosition.z / 2);

        // Start module.
        StartCoroutine(WaitForVideoClips());
    }

    /// <summary>
    /// Button initialisation.
    /// </summary>
    private void Awake()
    {
        // Press.
        Button.OnInteract += delegate ()
        {
            HandlePress();
            return false;
        };

        // Release.
        Button.OnInteractEnded += delegate ()
        {
            HandleRelease();
        };
    }

    /// <summary>
    /// Button hold handler.
    /// </summary>
    private void FixedUpdate()
    {
        float offset = Time.time * 0.01f;
        Foreground.material.mainTextureOffset = new Vector2(offset, -offset);

        // Rotates it as long as the module isn't solved.
        for (byte i = 0; i < Spinnables.Length; i++)
            Spinnables[i].localRotation = Quaternion.Euler(86 * Convert.ToByte(i != 2), _y += _rotationSpeed * Convert.ToSByte(!isSolved) * ((2 * Convert.ToSByte(_canBeOctOS)) - 1), 0);

        // Changes color back.
        for (byte i = 0; i < Cylinders.Length; i++)
        {
            Cylinders[i].material.color = new Color32(
                (byte)((Cylinders[i].material.color.r * 255) - (Convert.ToByte(Cylinders[i].material.color.r * 255 > 85) * 2)),
                (byte)((Cylinders[i].material.color.g * 255) - (Convert.ToByte(Cylinders[i].material.color.g * 255 > 85) * 2)),
                (byte)((Cylinders[i].material.color.b * 255) - (Convert.ToByte(Cylinders[i].material.color.b * 255 > 85) * 2)), 255);
        }

        // Increment the amount of frames of the user holding the button.
        if (_lightsOn && !isSolved && _isHolding)
            _held++;

        // Indicates that it is ready.
        Number.color = HexOSStrings.PerfectColors[1 + Convert.ToByte(_held >= 25)];

        if (_held == 25)
        {
            Audio.PlaySoundAtTransform("ready", Module.transform);
            Status.text = "Boot Manager\nStoring " + _submit + "...";
        }

        // Autoreset
        else if (_held == 125)
        {
            Audio.PlaySoundAtTransform("cancel", Module.transform);
            Status.text = "Boot Manager\nCancelling...";
            _isHolding = false;
            _held = -1;
        }
    }

    /// <summary>
    /// Lights get turned on.
    /// </summary>
    private void Activate()
    {
        // Plays the foreground video as decoration.
        VideoGrid.clip = Application.isEditor ? Clips[0] : VideoLoader.clips[0];
        VideoGrid.Prepare();
        VideoGrid.Play();

        // Reset the textures just in case
        Background.material.SetColor("_Color", HexOSStrings.TransparentColors[2]);
        Foreground.material.SetColor("_Color", Color.blue);

        // Get the correct answer.
        _answer = HexGenerate();

        // Add leading 0's.
        while (_answer.Length < 3)
            _answer = "0" + _answer;

        Debug.LogFormat("[hexOS #{0}]: The expected answer is {1}.", _moduleId, _answer);
        Status.text = "Boot Manager\nWaiting...";
        _lightsOn = true;
    }

    /// <summary>
    /// Button interaction, and handling of the cycled chords/sequences.
    /// </summary>
    private void HandlePress()
    {
        // Sounds and punch effect.
        Audio.PlaySoundAtTransform("click", Module.transform);
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, Module.transform);

        Button.AddInteractionPunch(3);

        // Lights off, solved then it should end it here.
        if (!_lightsOn || isSolved || _octAnimating)
            return;

        // Is now holding button.
        _isHolding = true;

        // Store the button press so that it wouldn't matter how long you hold on to the button.
        if (!_playSequence)
            _submit = Number.text;
    }

    /// <summary>
    /// Button interaction, and handling of the action depending on how long the button was held.
    /// </summary>
    private void HandleRelease()
    {
        // Is no longer holding button.
        _isHolding = false;

        // Lights off, solved, or playing sequence should end it here.
        if (!_lightsOn || isSolved || _octAnimating)
            return;

        if (!_playSequence)
            Status.text = "Boot Manager\nWaiting...";

        // If the button was held for less than 25 frames (0.5 seconds), then play the sequence.
        if (_held < 20)
        {
            // If the input was cancelled, don't play the sequence.
            if (_held < 0)
                return;

            // Reset holding.
            _held = 0;

            // If the sequence isn't already playing, play it.
            if (!_playSequence)
            {
                // Increment presses so that the correct chords and sequences are played.
                _press = (sbyte)((_press + 1) % 8);

                if (!_octOS)
                    StartCoroutine("HexPlaySequence");
                else
                    StartCoroutine("OctPlaySequence");
            }
        }

        // Otherwise, submit the answer that displayed when the button was pushed.
        else
        {
            Audio.PlaySoundAtTransform("submit", Module.transform);

            // Reset holding.
            _held = 0;

            // Add digit to user input only if the number exists.
            if (_submit[0] != ' ')
                _user += _submit[_user.Length];

            // If the user input has 3 inputs, check for answer.
            if (_user.Length == 3)
            {
                // Color each cylinder depending on which corresponding digit is correct.
                for (byte i = 0; i < Cylinders.Length; i++)
                {
                    if (_user == _octAnswer && _octOS)
                        Cylinders[i].material.color = new Color32(222, 222, 222, 255);

                    else if (_user[i] == _answer[i] && !(_user == "888" && _answer != "888"))
                        Cylinders[i].material.color = new Color32(51, 222, 51, 255);

                    else
                        Cylinders[i].material.color = new Color32(255, 51, 51, 255);
                }

                // User matched the expected answer, solve.
                if (_user == _answer && !_octOS)
                {
                    // This solves the module.
                    StartCoroutine("HexSolve");
                }

                else if (_user == _octAnswer && _octOS)
                {
                    // This solves the module in hard mode.
                    StartCoroutine("OctSolve");
                }

                // If the user activates hard mode.
                else if (_user == "888" && !_hasPlayedSequence && !_octOS && _canBeOctOS)
                {
                    Status.text = "Boot Manager\n...?";
                    Audio.PlaySoundAtTransform("octActivate", Module.transform);
                    
                    _octOS = true;
                    _user = "";

                    Background.material.SetColor("_Color", HexOSStrings.TransparentColors[0]);
                    Foreground.material.SetColor("_Color", Color.red);

                    // Generate new answer.
                    _octAnswer = OctGenerate();
                    Debug.LogFormat("[hexOS #{0}]: The expected answer for the current octOS is {1}.", _moduleId, _octAnswer);
                }

                // Otherwise, strike and reset the user input.
                else
                {
                    Debug.LogFormat("[hexOS #{0}]: The number submitted ({1}) did not match the expected answer ({2}), that's a strike!", _moduleId, _user, _answer);
                    _user = "";

                    if (!_octOS)
                    {
                        Audio.PlaySoundAtTransform("strike", Module.transform);
                        Status.text = "Boot Manager\nError!";
                        
                        // Caps at 1, 20+ are treated the same as exactly 20 strikes
                        _hasPlayedSequence = false;
                        _hexOSStrikes = Math.Min(++_hexOSStrikes, 20);

                        Module.HandleStrike();
                    }
                    
                    else
                        StartCoroutine("OctStrike");
                }
            }

            UserNumber.text = _user;

            while (UserNumber.text.Length != 3)
                UserNumber.text += '-';
        }
    }

    /// <summary>
    /// Solves the module when run. This stops ALL coroutines.
    /// </summary>
    private IEnumerator HexSolve()
    {
        // Typical module handle pass.
        Background.material.SetColor("_Color", HexOSStrings.TransparentColors[1]);
        Foreground.material.SetColor("_Color", Color.green);
        isSolved = true;
        Status.text = "Boot Manager\nUnlocked!";
        Debug.LogFormat("[hexOS #{0}]: The correct number was submitted, module solved!", _moduleId);
        Module.HandlePass();

        Button.AddInteractionPunch(15);

        // If forget the colors exists, or it's forced to, pick a meme message.
        if (_forceAltSolve || Info.GetSolvableModuleNames().Contains("Forget The Colors"))
        {
            Audio.PlaySoundAtTransform("solveAlt", Module.transform);
            Quote.text = HexOSStrings.AltSolvePhrases[Rnd.Range(0, HexOSStrings.AltSolvePhrases.Length)];
        }

        // Otherwise pick a regular message.
        else
        {
            Audio.PlaySoundAtTransform("solve", Module.transform);
            Quote.text = HexOSStrings.SolvePhrases[Rnd.Range(0, HexOSStrings.SolvePhrases.Length)];
        }

        // If custom quote has been filled, render it.
        if (_customSolveQuote != "")
            Quote.text = _customSolveQuote;

        // Shuffles through a bunch of random numbers.
        for (byte i = 0; i < 20; i++)
        {
            Number.text = Rnd.Range(100, 1000).ToString();
            yield return new WaitForSeconds(0.05f);
        }

        // Stops everything.
        Number.text = "---";

        // Goes through 3-255 and stops after overflow.
        for (byte i = 3; i > 2; i += 2)
        {
            Quote.color = new Color32(i, i, i, 255);
            yield return new WaitForSeconds(0.02f);
        }

        StopAllCoroutines();
    }

    /// <summary>
    /// Solves the module when run in hard mode. This stops ALL coroutines.
    /// </summary>
    private IEnumerator OctSolve()
    {
        _octAnimating = true;
        yield return new WaitForEndOfFrame();

        // Prevents the text from updating.
        StopCoroutine("UpdateScreen");

        // Sets the background and foreground to be white in case if the video animation is slightly delayed.
        Background.material.SetColor("_Color", HexOSStrings.TransparentColors[3]);
        Foreground.material.SetColor("_Color", Color.white);

        // Resets all strings.
        UserNumber.text = "";
        Number.text = "";
        Status.text = "";

        // Gives powerful emphasis.
        Button.AddInteractionPunch(25);

        // Plays the solve animation.
        VideoOct.transform.localPosition = new Vector3(0, 0.84f, 0);
        VideoRenderer.material.color = new Color32(255, 255, 255, 255);
        VideoOct.clip = Application.isEditor ? Clips[1] : VideoLoader.clips[1];
        VideoOct.Prepare();
        VideoOct.Play();

        Audio.PlaySoundAtTransform("octSolve", Module.transform);

        // The exact amount of seconds for the audio clip to go quiet is 10.122 seconds.
        yield return new WaitForSeconds(10.122f);

        Debug.LogFormat("[hexOS #{0}]: The correct number for octOS was submitted, module solved! +24 additional points!", _moduleId);
        isSolved = true;
        Module.HandlePass();
        StopAllCoroutines();
    }

    /// <summary>
    /// Strikes the module in an animation for hard mode.
    /// </summary>
    private IEnumerator OctStrike()
    {
        _octAnimating = true;
        yield return new WaitForEndOfFrame();

        // Prevents the text from updating.
        StopCoroutine("UpdateScreen");

        // Resets all strings.
        UserNumber.text = "";
        Number.text = "";
        Status.text = "";

        // Sets the background and foreground to be white in case if the video animation is slightly delayed.
        Background.material.SetColor("_Color", HexOSStrings.TransparentColors[3]);
        Foreground.material.SetColor("_Color", Color.white);

        VideoOct.transform.localPosition = new Vector3(0, 0.84f, 0);
        VideoRenderer.material.color = new Color32(255, 255, 255, 0);
        
        // Long animation.
        if (!_fastStrike)
        {
            VideoOct.clip = Application.isEditor ? Clips[2] : VideoLoader.clips[2];
            VideoOct.Prepare();
            VideoOct.Play();
            Audio.PlaySoundAtTransform("octStrike", Module.transform);

            byte c = 248;
            VideoRenderer.material.color = new Color32(255, 255, 255, c);

            while (c > 128)
            {
                c -= 20;
                VideoRenderer.material.color = new Color32(255, 255, 255, c);
                yield return new WaitForSeconds(0.1f);
            }

            while (c != 252)
            {
                c += 4;
                VideoRenderer.material.color = new Color32(255, 255, 255, c);
                yield return new WaitForSeconds(1.484375f);
            }

            yield return new WaitWhile(() => VideoOct.isPlaying);
        }

        // Short animation.
        else
        {
            VideoOct.clip = Application.isEditor ? Clips[3] : VideoLoader.clips[3];
            VideoOct.Prepare();
            VideoOct.Play();
            Audio.PlaySoundAtTransform("octStrikeFast", Module.transform);
            // For reference, the audio clip is 11.85 seconds.
            byte c = 0;
            while (c != 255)
            {
                c++;
                VideoRenderer.material.color = new Color32(255, 255, 255, c);
                yield return new WaitForSeconds(0.04f);
            }
            yield return new WaitWhile(() => VideoOct.isPlaying);
        }

        VideoOct.transform.localPosition = new Vector3(0, -0.42f, 0);

        // Reset back to hexOS, restoring all the values.
        _octOS = false;
        ModelName.text = "hexOS";
        screen = _tempScreen;
        UserNumber.text = "---";
        Status.text = "Boot Manager\nWaiting...";

        decipher = new char[2];
        for (int i = 0; i < decipher.Length; i++)
            _tempDecipher[i] = decipher[i];

        Background.material.SetColor("_Color", HexOSStrings.TransparentColors[2]);
        Foreground.material.SetColor("_Color", Color.blue);

        // Start it up again.
        _octAnimating = false;
        StartCoroutine("UpdateScreen");
        Module.HandleStrike();
    }

    /// <summary>
    /// Updates the screen every second to cycle all digits.
    /// </summary>
    private IEnumerator UpdateScreen()
    {
        byte index = 0;

        // While not solved, cycle through 30 digit number.
        while (!isSolved)
        {
            // If in last index, put a pause and restart loop.
            if (index >= screen.Length)
            {
                index = 0;
                Number.text = "   ";
            }

            // Otherwise, display next 3 digits.
            else
                Number.text = screen[index++].ToString() + screen[index++].ToString() + screen[index++].ToString();

            // Display lag.
            yield return new WaitForSeconds(1f + (_hexOSStrikes / 20) - (Convert.ToSingle(_octOS) / 1.5f));
        }
    }

    /// <summary>
    /// Play the sequence of notes and flashes on the module.
    /// </summary>
    private IEnumerator HexPlaySequence()
    {
        // The harder version can be activated only when the sequence hasn't been played yet.
        _hasPlayedSequence = true;

        // Prevent button presses from playing the sequence when it's already being played.
        _playSequence = true;

        byte[] seq1 = new byte[19], seq2 = new byte[19], seq3 = new byte[19];

        // Allow for easy access to all three via indexes.
        byte[][] seqs = new byte[3][] { seq1, seq2, seq3 };

        // Establish colors to be displayed for each tile, 0 = black, 1 = white, 2 = magenta.
        for (byte i = 0; i < _flashOtherColors - _hexOSStrikes; i++)
            // For each color.
            for (byte j = 0; j < 3; j++)
                // For each sequence variable.
                for (byte k = 0; k < seqs.Length; k++)
                    seqs[k][(i * 3) + j] = j;

        // Fill in remaining slots.
        for (byte i = (byte)Math.Max(3 * (_flashOtherColors - _hexOSStrikes), 0); i < seq1.Length; i++)
            // For each sequence variable.
            for (byte j = 0; j < seqs.Length; j++)
                seqs[j][i] = _ciphers[(_press % 2 * 3) + j];

        // Shuffle it for ambiguity.
        seq1.Shuffle();
        seq2.Shuffle();
        seq3.Shuffle();

        if (Status.text != "Boot Manager\nSaving " + _submit + "...")
            Status.text = "Boot Manager\nPlaying...";

        // Show cipher squares.
        for (byte i = 0; i < Ciphers.Length; i++)
            Ciphers[i].transform.localPosition = new Vector3(Ciphers[i].transform.localPosition.x, 0.21f, Ciphers[i].transform.localPosition.z);

        for (byte i = 0; i < HexOSStrings.Notes[_press].Length; i++)
        {
            // At least 2 strikes, start playing hi-hat.
            if (_delayPerBeat + (_hexOSStrikes / 20) > 0.2f)
                Audio.PlaySoundAtTransform("hihat", Module.transform);

            // Look through the sequence of rhythms, if a note should be playing, play note.
            if (HexOSStrings.Notes[_rhythms[_press % 2]][i] == 'X')
            {
                Audio.PlaySoundAtTransform("chord" + (_press + 1 + (Convert.ToByte(_octOS) * 8)), Module.transform);
                if (_experimentalShake)
                    Button.AddInteractionPunch();
            }

            // Render color, but only half as often as the rhythms.
            for (byte j = 0; j < Ciphers.Length; j++)
                Ciphers[j].material.color = HexOSStrings.PerfectColors[seqs[j][i / 2]];

            // If it's the last index, emphasise it with percussion.
            if (i == HexOSStrings.Notes[_press].Length - 1)
            {
                Audio.PlaySoundAtTransform("clap", Module.transform);

                if (_experimentalShake)
                    Button.AddInteractionPunch(10);

                if (Status.text != "Boot Manager\nStoring " + _submit + "...")
                    Status.text = "Boot Manager\nLoading...";
            }

            yield return new WaitForSeconds(Math.Min(_delayPerBeat + (_hexOSStrikes / 20), 1));
        }

        // Hide ciphers.
        for (byte j = 0; j < Ciphers.Length; j++)
        {
            Ciphers[j].material.color = new Color32(0, 0, 0, 255);
            Ciphers[j].transform.localPosition = new Vector3(Ciphers[j].transform.localPosition.x, -2.1f, Ciphers[j].transform.localPosition.z);
        }

        yield return new WaitForSeconds(Math.Min((_delayPerBeat * 12) + (_hexOSStrikes / 20), 1));

        if (Status.text != "Boot Manager\nStoring " + _submit + "...")
            Status.text = "Boot Manager\nWaiting...";

        // Allow button presses.
        _playSequence = false;
    }

    /// <summary>
    /// Play the hard mode's sequence of notes and flashes on the module.
    /// </summary>
    private IEnumerator OctPlaySequence()
    {
        // Prevent button presses from playing the sequence when it's already being played.
        _playSequence = true;

        if (Status.text != "Boot Manager\nSaving " + _submit + "...")
            Status.text = "Boot Manager\nPlaying...";

        // Show ciphers.
        for (byte i = 0; i < Ciphers.Length; i++)
            Ciphers[i].transform.localPosition = new Vector3(Ciphers[i].transform.localPosition.x, 0.21f, Ciphers[i].transform.localPosition.z);

        byte[,] seq = new byte[18, 9];

        // Array initializer.
        for (byte i = 0; i < seq.GetLength(0); i++)
        {
            // Fills in distracting lights.
            for (byte j = 0; j < 6; j++)
                seq[i, j] = (byte)(j / 2 * 12);

            // ULTRA-CRUEL VARIANT (literally unplayable garbage, don't use this)
            // Fills in 1 with incorrect color.
            //seq[i, 7] = (byte)(12 * (_octColors[i] + 1) % 3);

            // Fills remainder with "true" colors.
            for (byte j = 6; j < seq.GetLength(1); j++)
                seq[i, j] = (byte)(12 * _octColors[i]);
        }

        // Shuffles the sequence.
        Shuffle(seq);

        for (byte i = 0; i < HexOSStrings.OctNotes[_press].Length; i++)
        {
            // Look through the sequence of rhythms, if a note should be playing, play note.
            if (HexOSStrings.OctNotes[_octRhythms[_press % 2]][i] == 'X')
            {
                Audio.PlaySoundAtTransform("chord" + (_press + 1 + (Convert.ToByte(_octOS) * 8)), Module.transform);
                if (_experimentalShake)
                    Button.AddInteractionPunch();
            }

            // Render color.
            for (byte j = 0; j < Ciphers.Length; j++)
            {
                Ciphers[j].material.color = Color.white;
                Ciphers[j].material.mainTexture = FrequencyTextures[seq[j + (i / 17 * 3) + (_press % 2 * 9), i % 17 / 2] + _octSymbols[j + (i / 17 * 3) + (_press % 2 * 9)]];
            }

            // If it's the last index, emphasise it with percussion.
            if (i == HexOSStrings.OctNotes[_press].Length - 1)
            {
                Audio.PlaySoundAtTransform("clap", Module.transform);

                if (_experimentalShake)
                    Button.AddInteractionPunch(10);

                if (Status.text != "Boot Manager\nStoring " + _submit + "...")
                    Status.text = "Boot Manager\nLoading...";
            }

            // Create the amount of dots corresponding to which group it is cycling through.
            GroupCounter.text = "";
            for (byte k = 0; k <= i / 17; k++)
                GroupCounter.text += '.';

            // 60 / 1140 (190bpm * 6beat)
            yield return new WaitForSeconds(0.0526315789474f);
        }

        // Reset text.
        GroupCounter.text = "";

        // Turn back to black.
        for (byte j = 0; j < Ciphers.Length; j++)
        {
            Ciphers[j].material.color = new Color32(0, 0, 0, 255);
            Ciphers[j].transform.localPosition = new Vector3(Ciphers[j].transform.localPosition.x, -2.1f, Ciphers[j].transform.localPosition.z);
        }   

        // (60 / 1140) * 12 (190bpm * 6beat * 12beat)
        //yield return new WaitForSeconds(0.63157894736f);

        if (Status.text != "Boot Manager\nStoring " + _submit + "...")
            Status.text = "Boot Manager\nWaiting...";

        // Allow button presses.
        _playSequence = false;
    }

    /// <summary>
    /// Generates an answer. This should only be run once at the beginning of the module.
    /// </summary>
    private string HexGenerate()
    {
        // Generate random rhythm indexes, making sure that neither are the same.
        _rhythms[0] = (byte)Rnd.Range(0, HexOSStrings.Notes.Length);
        do _rhythms[1] = (byte)Rnd.Range(0, HexOSStrings.Notes.Length);
        while (_rhythms[1] == _rhythms[0]);

        Debug.LogFormat("[hexOS #{0}]: The first rhythm sequence is {1}.", _moduleId, HexOSStrings.Notes[_rhythms[0]]);
        Debug.LogFormat("[hexOS #{0}]: The second rhythm sequence is {1}.", _moduleId, HexOSStrings.Notes[_rhythms[1]]);

        // Generate random ciphers.
        for (byte i = 0; i < _ciphers.Length; i++)
            _ciphers[i] = (byte)Rnd.Range(0, 3);

        string[] logColor = { "Black", "White", "Magenta" };
        Debug.LogFormat("[hexOS #{0}]: Perfect Cipher is {1}, {2}, {3}, and {4}, {5}, {6}.", _moduleId, logColor[_ciphers[0]], logColor[_ciphers[1]], logColor[_ciphers[2]], logColor[_ciphers[3]], logColor[_ciphers[4]], logColor[_ciphers[5]]);

        // Generate numbers 0-9 for each significant digit.
        byte[,] temp = new byte[3, 10]
        {
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 },
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 },
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }
        };

        // Shuffles each array.
        Shuffle(temp);

        // Add it to the screen variable so that it's ready to be displayed.
        for (byte i = 0; i < 10; i++)
            for (byte j = 0; j < 3; j++)
                screen += temp[j, i];

        // Stores the current screen display in case if it gets replaced by octOS and needs to be reverted.
        _tempScreen = screen;

        // Thumbnail.
        //screen = "420420420420420420420420420420";

        Debug.LogFormat("[hexOS #{0}]: The screen displays the number {1}.", _moduleId, screen);
        StartCoroutine("UpdateScreen");

        // Converts indexes to binary which is how it is shown in the manual.
        string[] rhythmLog = { Convert.ToString(_rhythms[0], 2), Convert.ToString(_rhythms[1], 2) };
        for (byte i = 0; i < rhythmLog.Length; i++)
            while (rhythmLog[i].Length < 4)
                rhythmLog[i] = "0" + rhythmLog[i];

        Debug.LogFormat("[hexOS #{0}]: The rhythm sequences translate to {1} and {2}.", _moduleId, Convert.ToString(_rhythms[0], 2), Convert.ToString(_rhythms[1], 2));

        // Creates the sum, ensuring that it stays 4 bits long.
        sum = (short.Parse(Convert.ToString(_rhythms[0], 2)) + short.Parse(Convert.ToString(_rhythms[1], 2))).ToString();
        while (sum.Length < 4)
            sum = "0" + sum;

        Debug.LogFormat("[hexOS #{0}]: The sum of the rhythm sequence is {1}.", _moduleId, sum);

        byte[] encipher = new byte[2];

        // Gets index for Perfect Cipher
        for (byte i = 0; i < encipher.Length; i++)
        {
            byte j = (byte)(i * 3);
            encipher[i] = (byte)(_ciphers[j] + (_ciphers[j + 1] * 3) + (_ciphers[j + 2] * 9));
        }

        // Gets value from Perfect Cipher Dictionary
        for (byte i = 0; i < encipher.Length; i++)
        {
            HexOSStrings.PerfectCipher.TryGetValue(encipher[i], out decipher[i]);
            _tempDecipher[i] = decipher[i];
        }

        Debug.LogFormat("[hexOS #{0}]: Perfect Cipher decrypts to {1} and {2}.", _moduleId, decipher[0], decipher[1]);

        byte n = 0;
        byte[] logicA = new byte[6], logicB = new byte[6];

        // Creates pairs.
        for (byte l = 0; l < 4; l++)
            for (byte r = (byte)(l + 1); r < 4; r++)
            {
                logicA[n] = byte.Parse(sum[l].ToString());
                logicB[n] = byte.Parse(sum[r].ToString());
                n++;
            }

        Dictionary<char, string> logicGateNames = new Dictionary<char, string>(27) { { ' ', "SUM" }, { 'A', "AND" }, { 'B', "NAND" }, { 'C', "XAND" }, { 'D', "COMPARISON" }, { 'E', "A=1 THEN B" }, { 'F', "SUM" }, { 'G', "EQUALITY" }, { 'H', "OR" }, { 'I', "NOR" }, { 'J', "XOR" }, { 'K', "GULLIBILITY" }, { 'L', "NA THEN NB" }, { 'M', "IMPLIES" }, { 'N', "IMPLIES" }, { 'O', "NA THEN NB" }, { 'P', "GULLIBILITY" }, { 'Q', "XOR" }, { 'R', "NOR" }, { 'S', "OR" }, { 'T', "EQUALITY" }, { 'U', "SUM" }, { 'V', "A=1 THEN B" }, { 'W', "COMPARISON" }, { 'X', "XAND" }, { 'Y', "NAND" }, { 'Z', "AND" } };
        Debug.LogFormat("[hexOS #{0}]: The pairs to use in logic gates {1} and {2} are {3}{4}, {5}{6}, {7}{8}, {9}{10}, {11}{12}, {13}{14}.", _moduleId, logicGateNames[decipher[0]], logicGateNames[decipher[1]], logicA[0], logicB[0], logicA[1], logicB[1], logicA[2], logicB[2], logicA[3], logicB[3], logicA[4], logicB[4], logicA[5], logicB[5]);

        sbyte[] logicOutput = new sbyte[12];

        // Logic gates.
        for (byte i = 0; i < logicA.Length; i++)
            for (byte j = 0; j < decipher.Length; j++)
            {
                switch (decipher[j])
                {
                    case 'A': // AND
                    case 'Z':
                        logicOutput[(i * 2) + j] = (sbyte)(Math.Min(logicA[i], logicB[i]) - 1);
                        break;

                    case 'B': // NAND
                    case 'Y':
                        logicOutput[(i * 2) + j] = (sbyte)(2 - Math.Min(logicA[i], logicB[i]) - 1);
                        break;

                    case 'C': // XAND
                    case 'X':
                        logicOutput[(i * 2) + j] = (sbyte)(Mathf.Clamp(logicA[i] + logicB[i], 0, 1) + Convert.ToByte(logicA[i] + logicB[i] == 4) - 1);
                        break;

                    case 'D': // COMPARISON
                    case 'W':
                        logicOutput[(i * 2) + j] = (sbyte)(Convert.ToByte(logicA[i] > logicB[i]) + Convert.ToByte(logicA[i] >= logicB[i]) - 1);
                        break;

                    case 'E': // A=1 THEN B
                    case 'V':
                        if (logicA[i] == logicB[i])
                            logicOutput[(i * 2) + j] = (sbyte)(logicA[i] - 1);
                        else if (logicB[i] != 1)
                            logicOutput[(i * 2) + j] = (sbyte)(logicB[i] - 1);
                        else
                            logicOutput[(i * 2) + j] = (sbyte)(logicA[i] - 1);
                        break;

                    case 'F': // SUM
                    case 'U':
                    case ' ':
                        logicOutput[(i * 2) + j] = (sbyte)(((logicA[i] + logicB[i] + 2) % 3) - 1);
                        break;

                    case 'G': // EQUALITY
                    case 'T':
                        logicOutput[(i * 2) + j] = (sbyte)((2 * Convert.ToByte(logicA[i] == logicB[i])) - 1);
                        break;

                    case 'H': // OR
                    case 'S':
                        logicOutput[(i * 2) + j] = (sbyte)(Math.Max(logicA[i], logicB[i]) - 1);
                        break;

                    case 'I': // NOR
                    case 'R':
                        logicOutput[(i * 2) + j] = (sbyte)((2 - Math.Max(logicA[i], logicB[i])) - 1);
                        break;

                    case 'J': // XOR
                    case 'Q':
                        if (logicA[i] == 1 || logicB[i] == 1)
                            logicOutput[(i * 2) + j] = 0;
                        else if (logicA[i] == logicB[i])
                            logicOutput[(i * 2) + j] = 1;
                        else
                            logicOutput[(i * 2) + j] = -1;
                        break;

                    case 'K': // GULLIBILITY
                    case 'P':
                        if (logicA[i] + logicB[i] == 2)
                            logicOutput[(i * 2) + j] = 0;
                        else if (logicA[i] + logicB[i] > 2)
                            logicOutput[(i * 2) + j] = 1;
                        else
                            logicOutput[(i * 2) + j] = -1;
                        break;

                    case 'L': // NA THEN NB
                    case 'O':
                        if (logicA[i] == 1)
                            logicOutput[(i * 2) + j] = 0;
                        else if (logicA[i] == logicB[i] || logicA[i] + logicB[i] == 3)
                            logicOutput[(i * 2) + j] = 1;
                        else
                            logicOutput[(i * 2) + j] = -1;
                        break;

                    case 'M': // IMPLIES
                    case 'N':
                        logicOutput[(i * 2) + j] = (sbyte)(Mathf.Clamp(4 - (logicA[i] + logicB[i]), 0, 2) - 1);
                        break;
                }
            }

        // Creates offset.
        sbyte offset = 0;
        for (byte i = 0; i < logicOutput.Length; i++)
            offset += logicOutput[i];

        // Calculates the digital root with the offset.
        string newScreen = "";
        for (byte i = 0; i < screen.Length; i++)
            newScreen += ((byte.Parse(screen[i].ToString()) + Math.Abs(offset) - 1) % 9) + 1;

        Debug.LogFormat("[hexOS #{0}]: The output from each logic computation is {1}", _moduleId, logicOutput.Join(", "));
        Debug.LogFormat("[hexOS #{0}]: Combining all of them gives the offset {1}.", _moduleId, offset);
        Debug.LogFormat("[hexOS #{0}]: The modified screen display is {1}.", _moduleId, newScreen);

        // Run the algorithm to compress the 30-digit number into 3, then returning it.
        return (short.Parse(HexThreeDigit(newScreen)) % 1000).ToString();
    }

    /// <summary>
    /// Generates an answer for hard mode. This should only be run once when activated.
    /// </summary>
    private string OctGenerate()
    {
        Debug.LogFormat("[hexOS #{0}]: octOS has been activated! Regenerating module...", _moduleId);
        ModelName.text = "octOS";

        // Generate random rhythm indexes, making sure that neither are the same.
        _octRhythms[0] = (byte)Rnd.Range(0, HexOSStrings.OctNotes.Length);
        do _octRhythms[1] = (byte)Rnd.Range(0, HexOSStrings.OctNotes.Length);
        while (_octRhythms[1] == _octRhythms[0]);

        // Converts sum from decimal to base 4.
        string strSum = ConvertBase((Convert.ToInt16(_octRhythms[0]) * 16) + _octRhythms[1], new char[] { '0', '1', '2', '3' });
        while (strSum.Length < 4)
            strSum = '0' + strSum;

        sbyte[] sum = new sbyte[4];
        for (byte i = 0; i < sum.Length; i++)
            sum[i] = (sbyte)char.GetNumericValue(strSum[i]);

        Debug.LogFormat("[hexOS #{0}]: The first rhythm sequence is {1}.", _moduleId, HexOSStrings.OctNotes[_octRhythms[0]]);
        Debug.LogFormat("[hexOS #{0}]: The second rhythm sequence is {1}.", _moduleId, HexOSStrings.OctNotes[_octRhythms[1]]);
        Debug.LogFormat("[hexOS #{0}]: The 4-bit sum is {1}.", _moduleId, strSum);

        // Generate random key from a piece of a phrase.
        string key = HexOSStrings.OctPhrases[Rnd.Range(0, HexOSStrings.OctPhrases.Length)];
        key = key.Remove(0, Rnd.Range(0, key.Length - 6));

        while (key.Length > 6)
            key = key.Substring(0, key.Length - 1);

        // Generate random symbols.
        for (byte i = 0; i < _octSymbols.Length; i++)
            _octSymbols[i] = (byte)Rnd.Range(0, 12);

        char[] encipheredKey = key.ToCharArray();
        decipher = new char[6];

        // Enciphers each letter with the symbols.
        for (byte i = 0; i < encipheredKey.Length; i++)
        {
            byte index = 0;
            for (byte j = 0; j < HexOSStrings.Alphabet.Length; j++)
                if (HexOSStrings.Alphabet[j] == encipheredKey[i])
                {
                    index = j;
                    break;
                }

            encipheredKey[i] = HexOSStrings.Alphabet[(index - HexOSStrings.Symbols[_octSymbols[i * 3]] - HexOSStrings.Symbols[_octSymbols[(i * 3) + 1]] - HexOSStrings.Symbols[_octSymbols[(i * 3) + 2]] + 27) % 27];
            decipher[i] = encipheredKey[i];
        }

        // Enciphers each letter into colors.
        for (byte i = 0; i < encipheredKey.Length; i++)
        {
            byte index = HexOSStrings.PerfectCipher.FirstOrDefault(x => x.Value == encipheredKey[i].ToString().ToUpper().ToCharArray()[0]).Key;
            string colors = ConvertBase(index, new char[] { '0', '1', '2' });

            while (colors.Length < 3)
                colors = '0' + colors;

            foreach (char color in colors.Reverse())
                _octColors.Add((byte)char.GetNumericValue(color));
        }

        List<string> log = new List<string>(0);
        for (int i = 0; i < _octSymbols.Length; i++)
            log.Add(HexOSStrings.Symbols[_octSymbols[i]].ToString());

        byte keyIndex = 0;

        // Finds the first instance of the phrase.
        for (byte i = 0; i < HexOSStrings.OctPhrases.Length; i++)
            for (byte j = 0; j < HexOSStrings.OctPhrases[i].Length - 5; j++)
            {
                string comparison = HexOSStrings.OctPhrases[i].Substring(j, 6);

                if (key == comparison)
                    keyIndex = (byte)(i + 1);
            }

        Debug.LogFormat("[hexOS #{0}]: The colors decipher the phrase \"{1}\".", _moduleId, encipheredKey.Join(""));
        Debug.LogFormat("[hexOS #{0}]: The symbols' values are {1}.", _moduleId, log.Join(", "));
        Debug.LogFormat("[hexOS #{0}]: The deciphered letters are \"{1}\".", _moduleId, key);
        Debug.LogFormat("[hexOS #{0}]: The value obtained from the key is \"{1}\".", _moduleId, keyIndex);

        // Generate numbers 0-9 for each significant digit.
        byte[,] temp = new byte[3, 10]
        {
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 },
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 },
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }
        };

        // Shuffles each array.
        Shuffle(temp);

        // Add it to the screen variable so that it's ready to be displayed.
        screen = "";
        for (byte i = 0; i < 10; i++)
            for (byte j = 0; j < 3; j++)
                screen += temp[j, i];

        // Thumbnail.
        //screen = "420420420420420420420420420420";

        Debug.LogFormat("[hexOS #{0}]: The screen displays the number {1}.", _moduleId, screen);

        string alpha = "", beta = "", gamma = "", delta = "";

        // Half of the screen string is alpha, and the other half is beta.
        for (int i = 0; i < screen.Length - 5; i += 6)
        {
            alpha += screen[i].ToString() + screen[i + 1].ToString() + screen[i + 2].ToString();
            beta += screen[i + 3].ToString() + screen[i + 4].ToString() + screen[i + 5].ToString();
        }

        Debug.LogFormat("[hexOS #{0}]: α = {1}.", _moduleId, alpha);
        Debug.LogFormat("[hexOS #{0}]: β = {1}.", _moduleId, beta);

        // Half of gamma is alpha, the other half is beta, with exceptions.
        for (int i = 0; i < alpha.Length; i++)
        {
            // Special PRODUCT case
            if (i > 0 && beta[i] == beta[i - 1])
                gamma += '*';

            // Special SUM case
            else if (i > 0 && alpha[i] == alpha[i - 1])
                gamma += '+';

            // Special EQUALITY case
            else if (Math.Abs(char.GetNumericValue(alpha[i]) - char.GetNumericValue(beta[i])) == 5)
                gamma += '=';

            // Add ALPHA
            else if (i % 2 == 0)
                gamma += alpha[i];

            // Add BETA
            else
                gamma += beta[i];

            // Special IMPLIES case, has to be checked last.
            if (i > 0 && gamma[i] == gamma[i - 1])
                gamma = gamma.Remove(gamma.Length - 1, 1) + '>';
        }

        Debug.LogFormat("[hexOS #{0}]: γ = {1}.", _moduleId, gamma);

        // Checks if NOT gate should be used.
        bool notGate = false;
        if (sum[0] == sum[2] || sum[1] == sum[3])
        {
            notGate = true;
            for (byte i = 0; i < sum.Length; i++)
                sum[i] = (sbyte)(4 - sum[i]);
        }

        string sumLog = "";

        // Logic gates.
        for (byte i = 0; i < gamma.Length; i++)
        {
            byte operand = 0;
            switch (gamma[i])
            {
                case '0': operand = (byte)Math.Min(sum[0], sum[1]); break; // AND
                case '1': operand = (byte)Math.Max(sum[0], sum[1]); break; // OR
                case '2': operand = (byte)(4 - Math.Min(sum[0], sum[1])); break; // NAND
                case '3': operand = (byte)(4 - Math.Max(sum[0], sum[1])); break; // NOR

                case '4': // XAND
                    if (sum[0] <= 1 && sum[1] <= 1)
                        operand = (byte)Math.Min(sum[0], sum[1]);
                    else if (sum[0] >= 3 && sum[1] >= 3)
                        operand = (byte)Math.Max(sum[0], sum[1]);
                    else
                        operand = 2;
                    break;

                case '5': // XOR
                    if (sum[0] <= 1 && sum[1] <= 1)
                        operand = (byte)Math.Min(sum[0], sum[1]);
                    else if (sum[0] >= 3 && sum[1] >= 3)
                        operand = (byte)(4 - Math.Max(sum[0], sum[1]));
                    else if (sum[0] <= 1 && sum[1] >= 3)
                        operand = (byte)Math.Max(4 - sum[0], sum[1]);
                    else if (sum[0] >= 3 && sum[1] <= 1)
                        operand = (byte)Math.Max(sum[0], 4 - sum[1]);
                    else
                        operand = 2;
                    break;

                case '6': operand = (byte)Mathf.Clamp(sum[0] - sum[1] + 2, 0, 4); break; // COMPARISON
                case '7': operand = (byte)Math.Max(4 - sum[0], sum[1]); break; // GULLIBILITY
                case '8': operand = (sum[0] % 2 == 1 && sum[1] == 2) || (sum[0] % 4 == 0 && sum[1] % 4 != 0) ? (byte)sum[0] : (byte)sum[1]; break; // A=2 THEN B

                case '9': // NA THEN NB
                    if (sum[0] % 4 == 0 && sum[1] % 4 == 0 && sum[0] == sum[1])
                        operand = 4;
                    else if ((sum[0] == 1 && sum[1] <= 1) || (sum[0] == 3 && sum[1] >= 3))
                        operand = 3;
                    else if (sum[0] == 2)
                        operand = 2;
                    else if ((sum[0] == 1 && sum[1] >= 2) || (sum[0] == 3 && sum[1] <= 2))
                        operand = 1;
                    else
                        operand = 0;
                    break;

                case '+': operand = (byte)((sum[0] + sum[1]) % 5); break; // SUM
                case '*': operand = (byte)((sum[0] * sum[1]) % 5); break; // PRODUCT

                case '>': // IMPLIES
                    if (sum[0] == 3)
                        operand = sum[1] == 4 ? (byte)1 : (byte)(sum[1] + 1);
                    else
                        operand = (byte)(4 - (Convert.ToByte(sum[1] % Mathf.Pow(2, sum[0]) == 0) * sum[0]));
                    break;

                case '=': // EQUALITY
                    operand = 4;
                    if (sum[0] / 4 != sum[1] / 4)
                        operand = 0;
                    else
                    {
                        if (sum[0] % 2 != sum[1] % 2)
                            operand -= 1;
                        if (sum[0] / 2 != sum[1] / 2)
                            operand -= 2;
                    }
                    break; 
            }

            for (byte j = 0; j < sum.Length - 1; j++) // Shift left
                sum[j] = sum[j + 1];

            if (sum[1] == operand) // If second and fourth operand are the same, find earliest unique number.
            {
                if (notGate) // Searches forward for smallest unique number.
                    for (byte j = 0; j <= 4; j++)
                        for (byte k = 0; j < sum.Length; j++)
                        {
                            if (sum[k] == j)
                                break;
                            if (k == sum.Length - 1)
                            {
                                operand = j;
                                goto foundNumber;
                            }
                        }

                else // Searches backwards for biggest unique number.
                    for (sbyte l = 4; l >= 0; l--)
                        for (byte m = 0; m < sum.Length; m++)
                        {
                            if (sum[m] == l)
                                break;
                            if (m == sum.Length - 1)
                            {
                                operand = (byte)l;
                                goto foundNumber;
                            }
                        }
            }

            // If it has found a number to override the operand with, this is where it will end up going.
            foundNumber:

            sum[3] = (sbyte)operand;

            // Logs the 4-bit sum.
            sumLog += sum.Join("");
            if (i != gamma.Length - 1)
                sumLog += ", ";

            // Delta is equal to the operand from gamma's logic gate applied to alpha and beta.
            switch (operand)
            {
                case 0: delta += char.GetNumericValue(alpha[i]) * char.GetNumericValue(beta[i]) % 10; break;
                case 1: delta += Math.Abs(char.GetNumericValue(alpha[i]) - char.GetNumericValue(beta[i])); break;
                case 2: delta += (char.GetNumericValue(alpha[i]) + char.GetNumericValue(beta[i])) % 10; break;
                case 3: delta += Math.Min(char.GetNumericValue(alpha[i]), char.GetNumericValue(beta[i])); break;
                case 4: delta += Math.Max(char.GetNumericValue(alpha[i]), char.GetNumericValue(beta[i])); break;
            }
        }

        Debug.LogFormat("[hexOS #{0}]: 4-bits = {1}.", _moduleId, sumLog);
        Debug.LogFormat("[hexOS #{0}]: δ = {1}.", _moduleId, delta);

        // Calculates the digital root.
        string screenWithKeyIndex = "", newScreen = "";
        for (byte i = 0; i < delta.Length; i++)
            screenWithKeyIndex += ((byte.Parse(delta[i].ToString()) + keyIndex - 1) % 9) + 1;

        Debug.LogFormat("[hexOS #{0}]: Applying offset {1} to δ = {2}.", _moduleId, keyIndex, screenWithKeyIndex);

        // Combine one-third with two-thirds.
        for (byte i = 0; i < screenWithKeyIndex.Length; i += 3)
        {
            newScreen += (char.GetNumericValue(screenWithKeyIndex[i]) + char.GetNumericValue(screenWithKeyIndex[i + 2])) % 10;
            newScreen += screenWithKeyIndex[i + 1];
        }

        Debug.LogFormat("[hexOS #{0}]: Adding left 2 digits with right digit: {1}.", _moduleId, newScreen);

        // Return the result of compressing a 15-digit number to a 3.
        return OctThreeDigit(newScreen);
    }

    /// <summary>
    /// An algorithm that takes a 30-digit number and compresses it to a 3- or 4-digit number to return as the answer of the module.
    /// </summary>
    /// <param name="seq">The sequence of digits that will be used.</param>
    private string HexThreeDigit(string seq)
    {
        Debug.LogFormat("[hexOS #{0}]: Current sequence > {1}", _moduleId, seq);

        // Create groups of 6.
        List<int> digits = new List<int>(0);
        for (byte i = 5; i < seq.Length; i += 6)
            digits.Add(int.Parse(string.Concat(seq[i - 5], seq[i - 4], seq[i - 3], seq[i - 2], seq[i - 1], seq[i])));

        Debug.LogFormat("[hexOS #{0}]: Forming groups > {1}", _moduleId, digits.Join(", "));
        seq = "";

        // Add groups of 6 with each other.
        for (byte i = 0; i < digits.Count; i++)
            seq += (digits[i] / 1000 + digits[i] % 1000).ToString();

        Debug.LogFormat("[hexOS #{0}]: Combining the groups > {1}", _moduleId, seq);

        // Get leftovers.
        string leftover = "";
        for (byte i = (byte)(Math.Floor(seq.Length / 6f) * 6); i < seq.Length && i != 0; i++)
            leftover += seq[i];

        string newSeq = "";

        if (leftover.Length != 0)
        {
            // Add leftovers to sequence with digital root.
            for (byte i = 0; i < (Math.Floor(seq.Length / 6f) * 6); i++)
                newSeq += ((((byte)(byte.Parse(seq[i].ToString()) + byte.Parse(leftover[i % leftover.Length].ToString()))) - 1) % 9) + 1;

            Debug.LogFormat("[hexOS #{0}]: Leftovers > {1}", _moduleId, leftover);
            Debug.LogFormat("[hexOS #{0}]: Modified sequence > {1}", _moduleId, newSeq);
        }

        else
        {
            Debug.LogFormat("[hexOS #{0}]: No leftovers. Continue as normal.", _moduleId);
            newSeq = seq;
        }

        // Repeat if equal or more than 6 digits long.
        if (newSeq.Length >= 6)
        {
            Debug.LogFormat("[hexOS #{0}]: Sequence is not less than 6 digits long. Repeat this process.", _moduleId);
            newSeq = HexThreeDigit(newSeq);
        }

        // Remove any additional digits.
        while (newSeq.Length > 3)
            newSeq = newSeq.Substring(1, newSeq.Length - 1);

        // Once you reach here, you have a 3-digit number!
        return newSeq;
    }

    /// <summary>
    /// An algorithm that takes a 30-digit number and compresses it to a 3-digit number to return as the answer of the module.
    /// </summary>
    /// <param name="seq">The sequence of digits that will be used.</param>
    private string OctThreeDigit(string seq)
    {
        string newSeq = "";
        Debug.LogFormat("[hexOS #{0}]: Sequence > {1}", _moduleId, seq);

        // Determine the last digit of the sequence
        switch (seq[seq.Length - 1])
        {
            case '0': // Divide by 10.
                newSeq = seq.Length > 4 ? seq.Substring(0, seq.Length - 2) + '8' : seq.Substring(0, seq.Length - 1);
                break;

            case '1': // Subtract digits with the last.
            case '4':
                for (byte i = 0; i < seq.Length; i++)
                    newSeq += ((char.GetNumericValue(seq[i]) - char.GetNumericValue(seq[seq.Length - 1]) + 10) % 10).ToString();
                break;

            case '2': // Add or subtract prime digits.
            case '5':
            case '7':
                for (byte i = 0; i < seq.Length; i++)
                    switch (i)
                    {
                        case 1:
                        case 2:
                        case 4:
                        case 6:
                            newSeq += ((char.GetNumericValue(seq[i]) + char.GetNumericValue(seq[seq.Length - 1])) % 10).ToString();
                            break;

                        default:
                            newSeq += ((char.GetNumericValue(seq[i]) - char.GetNumericValue(seq[seq.Length - 1]) + 10) % 10).ToString();
                            break;
                    }
                break;

            case '3': // Replace with 0 or 7.
            case '6':
            case '9':
                for (int i = 0; i < seq.Length; i++)
                {
                    if (seq[i] == '3' || seq[i] == '6' || seq[i] == '9')
                        newSeq += (i + 1) % 3 == 0 ? '0' : '7';

                    else
                        newSeq += seq[i];
                }
                break;

            case '8': // Add neighbouring numbers.
                for (byte i = 0; i < seq.Length; i++)
                {
                    byte temp = 0;
                    temp += (byte)char.GetNumericValue(seq[i]);
                    if (i != 0)
                        temp += (byte)char.GetNumericValue(seq[i - 1]);
                    if (i != seq.Length - 1)
                        temp += (byte)char.GetNumericValue(seq[i + 1]);
                    newSeq += (temp % 10).ToString();
                }
                break;
        }

        // Rerun the algorithm if the sequence isn't 3 digits long, otherwise finish!
        return newSeq.Length == 3 ? newSeq : OctThreeDigit(newSeq);
    }

    /// <summary>
    /// Converts from base 10 to any base.
    /// </summary>
    public static string ConvertBase(int value, char[] baseChars)
    {
        // 32 is the worst cast buffer size for base 2 and int.MaxValue
        byte i = 32;
        char[] buffer = new char[i];
        byte targetBase = (byte)baseChars.Length;

        do
        {
            buffer[--i] = baseChars[value % targetBase];
            value = value / targetBase;
        }
        while (value > 0);

        char[] result = new char[32 - i];
        Array.Copy(buffer, i, result, 0, 32 - i);

        return new string(result);
    }

    /// <summary>
    /// Shuffles the nested array randomly by swapping random indexes with each other.
    /// </summary>
    /// <typeparam name="T">The element type of the array.</typeparam>
    /// <param name="array">The nested array to shuffle.</param>
    private static void Shuffle<T>(T[,] array)
    {
        for (byte i = 0; i < array.GetLength(0); i++)
            for (byte j = 0; j < array.GetLength(1); j++)
            {
                byte rnd = (byte)Rnd.Range(0, array.GetLength(1));

                T temp = array[i, j];
                array[i, j] = array[i, rnd];
                array[i, rnd] = temp;
            }
    }

    /// <summary>
    /// Determines whether the input from the TwitchPlays chat command is valid. Valid inputs are numbers from 0 to 999.
    /// </summary>
    /// <param name="par">The string from the user.</param>
    private bool IsValid(string par)
    {
        ushort s;
        return ushort.TryParse(par, out s) && s < 1000;
    }

#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"!{0} play (Plays the sequence provided by the module.) - !{0} submit <###> (Submits the number by holding the button at those specific times. | Valid numbers range from 0-999 | Example: !{0} submit 420)";
#pragma warning restore 414

    /// <summary>
    /// TwitchPlays Compatibility, detects every chat message and clicks buttons accordingly.
    /// </summary>
    /// <param name="command">The twitch command made by the user.</param>
    IEnumerator ProcessTwitchCommand(string command)
    {
        // Splits each command by spaces.
        string[] user = command.Split(' ');

        // If command is formatted correctly.
        if (Regex.IsMatch(user[0], @"^\s*play\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return null;

            // Sequence is already playing.
            if (_playSequence)
                yield return "sendtochaterror The sequence is already being played! Wait until the sequence is over!";

            // This command is valid, play sequence.
            else
            {
                Button.OnInteract();
                Button.OnInteractEnded();
            }
        }

        // If command is formatted correctly.
        else if (Regex.IsMatch(user[0], @"^\s*submit\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return null;

            // No number.
            if (user.Length < 2)
                yield return "sendtochaterror A number must be specified! (Valid: 0-999)";

            // More than one number.
            else if (user.Length > 2)
                yield return "sendtochaterror Only one number must be specified! (Valid: 0-999)";

            // Number outside range.
            else if (!IsValid(user.ElementAt(1)))
                yield return "sendtochaterror Number wasn't in range! (Valid: 0-999)";

            // If command is valid, push button accordingly.
            else
            {
                // Add leading 0's.
                while (user[1].Length < 3)
                    user[1] = "0" + user[1];

                // Will quickly determine if the module is about to solve or strike.
                if ((!_octOS && user[1] == _answer) || (_octOS && user[1] == _octAnswer))
                    yield return "solve";

                else if (user[1] == "888" && !_hasPlayedSequence && !_octOS && _canBeOctOS)
                    yield return null;

                else
                    yield return "strike";

                // Cycle through each digit.
                for (byte i = 0; i < user[1].Length; i++)
                {
                    // Wait until the correct number is shown.
                    yield return new WaitWhile(() => user[1][i] != Number.text[i]);

                    // Hold button.
                    Button.OnInteract();

                    // Wait until module can submit.
                    yield return new WaitWhile(() => _held < 20);

                    // Release button.
                    Button.OnInteractEnded();
                }

                if (_octOS && user[1] == _octAnswer)
                    yield return "awardpoints 24";
            }
        }
    }

    /// <summary>
    /// Force the module to be solved in TwitchPlays
    /// </summary>
    IEnumerator TwitchHandleForcedSolve()
    {
        yield return null;

        // Get the correct answer.
        string solve = _octOS ? _octAnswer : _answer;

        Debug.LogFormat("[hexOS #{0}]: Admin has initiated autosolver. The module will now submit {1}.", _moduleId, solve);

        // Cycle through each digit.
        for (byte i = 0; i < solve.Length; i++)
        {
            // Wait until the correct number is shown.
            yield return new WaitWhile(() => solve[i] != Number.text[i]);

            // Hold button.
            Button.OnInteract();

            // Wait until module can submit.
            yield return new WaitWhile(() => _held < 20);

            // Release button.
            Button.OnInteractEnded();
        }
    }
}