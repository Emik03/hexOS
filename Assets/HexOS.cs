using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Rnd = UnityEngine.Random;

public class HexOS : MonoBehaviour
{
    public KMAudio Audio;
    public KMBombInfo Info;
    public KMBombModule Module;
    public KMSelectable Button;
    public MeshRenderer[] Ciphers, Cylinders;
    public TextMesh Number, UserNumber, Status, Quote;
    public Transform[] Star;

    bool isSolved = false;
    readonly char[] decipher = new char[2];
    string sum = "", screen = "";

    private bool _lightsOn = false, _isHolding, _playSequence;
    private sbyte _press = -1, _held = 0;
    private readonly byte[] _rhythms = new byte[2], _ciphers = new byte[6];
    private static int _moduleIdCounter = 1, _y = 0;
    private int _moduleId = 0;
    private string _user = "", _answer = "", _submit = "";

    /// <summary>
    /// moduleId Initialisation
    /// </summary>
    private void Start()
    {
        Module.OnActivate += Activate;
        _moduleId = _moduleIdCounter++;
    }

    /// <summary>
    /// Button initialisation.
    /// </summary>
    private void Awake()
    {
        //press
        Button.OnInteract += delegate ()
        {
            HandlePress();
            return false;
        };

        //release
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
        //rotates it as long as the module isn't solved
        for (byte i = 0; i < Star.Length; i++)
            Star[i].localRotation = Quaternion.Euler(86, _y += Convert.ToByte(!isSolved), 0);

        //changes color back
        for (byte i = 0; i < Cylinders.Length; i++)
        {
            Cylinders[i].material.color = new Color32(
                (byte)((Cylinders[i].material.color.r * 255) - (Convert.ToByte(Cylinders[i].material.color.r * 255 > 85) * 2)),
                (byte)((Cylinders[i].material.color.g * 255) - (Convert.ToByte(Cylinders[i].material.color.g * 255 > 85) * 2)),
                (byte)((Cylinders[i].material.color.b * 255) - (Convert.ToByte(Cylinders[i].material.color.b * 255 > 85) * 2)), 255);
        }

        //increment the amount of frames of the user holding the button
        if (_lightsOn && !isSolved && _isHolding)
            _held++;

        //indicates that it is ready
        Number.color = _color32s[1 + Convert.ToByte(_held >= 25)];

        if (_held == 25)
        {
            Audio.PlaySoundAtTransform("ready", Module.transform);
            Status.text = "Boot Manager\nStoring " + _submit + "...";
        }

        //autoreset
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
        //get the correct answer
        _answer = Generate();

        //add leading 0's
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
        //sounds and punch effect
        Audio.PlaySoundAtTransform("click", Module.transform);
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, Module.transform);
        Button.AddInteractionPunch();

        //lights off, solved then it should end it here
        if (!_lightsOn || isSolved)
            return;

        //is now holding button
        _isHolding = true;

        //store the button press so that it wouldn't matter how long you hold on to the button
        if (!_playSequence)
            _submit = Number.text;
    }

    /// <summary>
    /// Button interaction, and handling of the action depending on how long the button was held.
    /// </summary>
    private void HandleRelease()
    {
        //is no longer holding button
        _isHolding = false;

        //lights off, solved, or playing sequence should end it here
        if (!_lightsOn || isSolved)
            return;

        if (!_playSequence)
            Status.text = "Boot Manager\nWaiting...";

        //if the button was held for less than 25 frames (0.5 seconds), then play the sequence
        if (_held < 20)
        {
            //if the input was cancelled, don't play the sequence
            if (_held < 0)
                return;

            //reset holding
            _held = 0;

            //if the sequence isn't already playing, play it
            if (!_playSequence)
            {
                //increment presses so that the correct chords and sequences are played
                _press = (sbyte)((_press + 1) % 8);

                StartCoroutine("PlaySequence");
            }
        }

        //otherwise, submit the answer that displayed when the button was pushed
        else
        {
            Audio.PlaySoundAtTransform("submit", Module.transform);

            //reset holding
            _held = 0;

            //add digit to user input only if the number exists
            if (_submit[0] != ' ')
                _user += _submit[_user.Length];

            //if the user input has 3 inputs, check for answer
            if (_user.Length == 3)
            {
                for (byte i = 0; i < Cylinders.Length; i++)
                {
                    if (_user[i] == _answer[i])
                        Cylinders[i].material.color = new Color32(51, 222, 51, 255);

                    else
                        Cylinders[i].material.color = new Color32(255, 51, 51, 255);
                }

                //user matched the expected answer, solve
                if (_user == _answer)
                {
                    //this solves the module
                    StartCoroutine("Solve");
                }

                //otherwise, strike and reset the user input
                else
                {
                    Audio.PlaySoundAtTransform("strike", Module.transform);
                    Status.text = "Boot Manager\nError!";
                    Debug.LogFormat("[hexOS #{0}]: The number submitted ({1}) did not match the expected answer ({2}), that's a strike!", _moduleId, _user, _answer);
                    _user = "";
                    Module.HandleStrike();
                }
            }

            //render text on module
            UserNumber.text = _user;

            while (UserNumber.text.Length != 3)
                UserNumber.text += '-';
        }
    }

    /// <summary>
    /// Solves the module when run. This stops ALL coroutines.
    /// </summary>
    private IEnumerator Solve()
    {
        //typical module handle pass
        isSolved = true;
        Status.text = "Boot Manager\nUnlocked!";
        Debug.LogFormat("[hexOS #{0}]: The correct number was submitted, module solved!", _moduleId);
        Module.HandlePass();

        //if forget the colors OR directional button exists, pick a meme message
        if (Info.GetSolvableModuleNames().Contains("Forget The Colors") || Info.GetSolvableModuleNames().Contains("Directional Button"))
        {
            Audio.PlaySoundAtTransform("solveAlt", Module.transform);
            Quote.text = _solveAlt[Rnd.Range(0, _solveAlt.Length)];
        }

        //otherwise pick a regular message
        else
        {
            Audio.PlaySoundAtTransform("solve", Module.transform);
            Quote.text = _solve[Rnd.Range(0, _solve.Length)];
        }

        //plays every sound
        for (byte i = 0; i < _solveChords.Length; i++)
        {
            Number.text = Rnd.Range(100, 1000).ToString();
            yield return new WaitForSeconds(0.05f);
        }

        //stops everything
        Number.text = "---";

        //goes through 3-255 and stops after overflow
        for (byte i = 3; i > 2; i += 2)
        {
            Quote.color = new Color32(i, i, i, 255);
            yield return new WaitForSeconds(0.02f);
        }

        StopAllCoroutines();
    }

    /// <summary>
    /// Updates the screen every second to cycle all 30 digit numbers.
    /// </summary>
    private IEnumerator UpdateScreen()
    {
        byte index = 0;

        //while not solved, cycle through 30 digit number
        while (!isSolved)
        {
            //if in last index, put a pause and restart loop
            if (index == 30)
            {
                index = 0;
                Number.text = "   ";
            }

            //otherwise, display next 3 digits
            else
                Number.text = screen[index++].ToString() + screen[index++].ToString() + screen[index++].ToString();

            //display lag
            yield return new WaitForSeconds(1f);
        }
    }

    /// <summary>
    /// Play the sequence of notes and flashes on the module.
    /// </summary>
    private IEnumerator PlaySequence()
    {
        //prevent button presses from playing the sequence when it's already being played
        _playSequence = true;

        //establish colors to be displayed for each tile, 0 = black, 1 = white, 2 = magenta
        byte[] seq1 = new byte[19] { 0, 0, 0, 0, 0,
                                     1, 1, 1, 1, 1,
                                     2, 2, 2, 2, 2,
                                     _ciphers[_press % 2 * 3], _ciphers[_press % 2 * 3], _ciphers[_press % 2 * 3], _ciphers[_press % 2 * 3] };
        byte[] seq2 = new byte[19] { 0, 0, 0, 0, 0,
                                     1, 1, 1, 1, 1,
                                     2, 2, 2, 2, 2,
                                     _ciphers[(_press % 2 * 3) + 1], _ciphers[(_press % 2 * 3) + 1], _ciphers[(_press % 2 * 3) + 1], _ciphers[(_press % 2 * 3) + 1] };
        byte[] seq3 = new byte[19] { 0, 0, 0, 0, 0,
                                     1, 1, 1, 1, 1,
                                     2, 2, 2, 2, 2,
                                     _ciphers[(_press % 2 * 3) + 2], _ciphers[(_press % 2 * 3) + 2], _ciphers[(_press % 2 * 3) + 2], _ciphers[(_press % 2 * 3) + 2] };

        //allow for easy access to all three via indexes
        byte[][] seq = new byte[3][] { seq1, seq2, seq3 };

        //shuffle it for ambiguity
        seq1.Shuffle();
        seq2.Shuffle();
        seq3.Shuffle();

        if (Status.text != "Boot Manager\nSaving " + _submit + "...")
            Status.text = "Boot Manager\nPlaying...";

        for (byte i = 0; i < _notes[_press].Length; i++)
        {
            //look through the sequence of rhythms, if a note should be playing, play note
            if (_notes[_rhythms[_press % 2]][i] == 'X')
                Audio.PlaySoundAtTransform("chord" + (_press + 1), Module.transform);

            //render color, but only half as often as the rhythms
            for (byte j = 0; j < Ciphers.Length; j++)
                Ciphers[j].material.color = _color32s[seq[j][i / 2]];

            //if it's the last index, emphasise it with percussion
            if (i == _notes[_press].Length - 1)
            {
                Audio.PlaySoundAtTransform("clap", Module.transform);

                if (Status.text != "Boot Manager\nStoring " + _submit + "...")
                    Status.text = "Boot Manager\nLoading...";
            }

            //60 / 1140 (190bpm * 6beat)
            //yield return new WaitForSeconds(0.0526315789474f);
            yield return new WaitForSeconds(0.07f);
        }

        //turn back to black
        for (byte j = 0; j < Ciphers.Length; j++)
            Ciphers[j].material.color = new Color32(0, 0, 0, 255);

        //(60 / 1140) * 12 (190bpm * 6beat * 12beat)
        //yield return new WaitForSeconds(0.63157894736f);
        yield return new WaitForSeconds(0.84f);

        if (Status.text != "Boot Manager\nStoring " + _submit + "...")
            Status.text = "Boot Manager\nWaiting...";

        //allow button presses
        _playSequence = false;
    }

    /// <summary>
    /// Generates an answer. This should only be run once at the beginning of the module.
    /// </summary>
    private string Generate()
    {
        //generate random rhythm indexes, making sure that neither are the same
        _rhythms[0] = (byte)Rnd.Range(0, _notes.Length);
        do _rhythms[1] = (byte)Rnd.Range(0, _notes.Length);
        while (_rhythms[1] == _rhythms[0]);

        Debug.LogFormat("[hexOS #{0}]: The first rhythm sequence is {1}.", _moduleId, _notes[_rhythms[0]]);
        Debug.LogFormat("[hexOS #{0}]: The second rhythm sequence is {1}.", _moduleId, _notes[_rhythms[1]]);

        //generate random ciphers
        for (byte i = 0; i < _ciphers.Length; i++)
            _ciphers[i] = (byte)Rnd.Range(0, 3);

        string[] logColor = { "Black", "White", "Magenta" };
        Debug.LogFormat("[hexOS #{0}]: Perfect Cipher is {1}, {2}, {3}, and {4}, {5}, {6}.", _moduleId, logColor[_ciphers[0]], logColor[_ciphers[1]], logColor[_ciphers[2]], logColor[_ciphers[3]], logColor[_ciphers[4]], logColor[_ciphers[5]]);

        //generate numbers 0-9 for each significant digit
        byte[] temp1 = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        byte[] temp2 = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        byte[] temp3 = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        //shuffle all of them
        temp1.Shuffle();
        temp2.Shuffle();
        temp3.Shuffle();

        //add it to the screen variable so that it's ready to be displayed
        for (byte i = 0; i < temp1.Length; i++)
        {
            screen += temp1[i];
            screen += temp2[i];
            screen += temp3[i];
        }

        //thumbnail
        //screen = "420420420420420420420420420420";

        Debug.LogFormat("[hexOS #{0}]: The screen displays the number {1}.", _moduleId, screen);
        StartCoroutine("UpdateScreen");

        //converts indexes to binary which is how it is shown in the manual
        string[] rhythmLog = { Convert.ToString(_rhythms[0], 2), Convert.ToString(_rhythms[1], 2) };
        for (byte i = 0; i < rhythmLog.Length; i++)
            while (rhythmLog[i].Length < 4)
                rhythmLog[i] = "0" + rhythmLog[i];

        Debug.LogFormat("[hexOS #{0}]: The rhythm sequences translate to {1} and {2}.", _moduleId, Convert.ToString(_rhythms[0], 2), Convert.ToString(_rhythms[1], 2));

        //creates the sum, ensuring that it stays 4 bits long
        sum = (short.Parse(Convert.ToString(_rhythms[0], 2)) + short.Parse(Convert.ToString(_rhythms[1], 2))).ToString();
        while (sum.Length < 4)
            sum = "0" + sum;

        Debug.LogFormat("[hexOS #{0}]: The sum of the rhythm sequence is {1}.", _moduleId, sum);

        byte[] encipher = new byte[2];

        for (byte i = 0; i < encipher.Length; i++)
        {
            byte v = (byte)(i * 3);
            encipher[i] = (byte)(_ciphers[v] + (_ciphers[v + 1] * 3) + (_ciphers[v + 2] * 9));
        }

        Dictionary<byte, char> perfectCipher = new Dictionary<byte, char>(27) { { 0, ' ' }, { 1, 'A' }, { 2, 'B' }, { 3, 'C' }, { 4, 'D' }, { 5, 'E' }, { 6, 'F' }, { 7, 'G' }, { 8, 'H' }, { 9, 'I' }, { 10, 'J' }, { 11, 'K' }, { 12, 'L' }, { 13, 'M' }, { 14, 'N' }, { 15, 'O' }, { 16, 'P' }, { 17, 'Q' }, { 18, 'R' }, { 19, 'S' }, { 20, 'T' }, { 21, 'U' }, { 22, 'V' }, { 23, 'W' }, { 24, 'X' }, { 25, 'Y' }, { 26, 'Z' } };

        for (byte i = 0; i < encipher.Length; i++)
            perfectCipher.TryGetValue(encipher[i], out decipher[i]);

        Debug.LogFormat("[hexOS #{0}]: Perfect Cipher decrypts to {1} and {2}.", _moduleId, decipher[0], decipher[1]);

        byte[] logicA = new byte[6], logicB = new byte[6];
        byte n = 0;
        for (byte l = 0; l < 4; l++)
        {
            for (byte r = (byte)(l + 1); r < 4; r++)
            {
                logicA[n] = byte.Parse(sum[l].ToString());
                logicB[n] = byte.Parse(sum[r].ToString());
                n++;
            }
        }

        Dictionary<char, string> logicGateNames = new Dictionary<char, string>(27) { { ' ', "SUM" }, { 'A', "AND" }, { 'B', "NAND" }, { 'C', "XAND" }, { 'D', "COMPARISON" }, { 'E', "A=1 THEN B" }, { 'F', "SUM" }, { 'G', "EQUALITY" }, { 'H', "OR" }, { 'I', "NOR" }, { 'J', "XOR" }, { 'K', "GULLIBILITY" }, { 'L', "NA THEN NB" }, { 'M', "IMPLIES" }, { 'N', "IMPLIES" }, { 'O', "NA THEN NB" }, { 'P', "GULLIBILITY" }, { 'Q', "XOR" }, { 'R', "NOR" }, { 'S', "OR" }, { 'T', "EQUALITY" }, { 'U', "SUM" }, { 'V', "A=1 THEN B" }, { 'W', "COMPARISON" }, { 'X', "XAND" }, { 'Y', "NAND" }, { 'Z', "AND" } };
        Debug.LogFormat("[hexOS #{0}]: The pairs to use in logic gates {1} and {2} are {3}{4}, {5}{6}, {7}{8}, {9}{10}, {11}{12}, {13}{14}.", _moduleId, logicGateNames[decipher[0]], logicGateNames[decipher[1]], logicA[0], logicB[0], logicA[1], logicB[1], logicA[2], logicB[2], logicA[3], logicB[3], logicA[4], logicB[4], logicA[5], logicB[5]);

        sbyte[] logicOutput = new sbyte[12];
        for (byte i = 0; i < logicA.Length; i++)
            for (byte j = 0; j < decipher.Length; j++)
            {
                switch (decipher[j])
                {
                    case 'A':
                    case 'Z':
                        logicOutput[(i * 2) + j] = (sbyte)(Math.Min(logicA[i], logicB[i]) - 1);
                        break;

                    case 'B':
                    case 'Y':
                        logicOutput[(i * 2) + j] = (sbyte)(2 - Math.Min(logicA[i], logicB[i]) - 1);
                        break;

                    case 'C':
                    case 'X':
                        logicOutput[(i * 2) + j] = (sbyte)(Mathf.Clamp(logicA[i] + logicB[i], 0, 1) + Convert.ToByte(logicA[i] + logicB[i] == 4) - 1);
                        break;

                    case 'D':
                    case 'W':
                        logicOutput[(i * 2) + j] = (sbyte)(Convert.ToByte(logicA[i] > logicB[i]) + Convert.ToByte(logicA[i] >= logicB[i]) - 1);
                        break;

                    case 'E':
                    case 'V':
                        if (logicA[i] == logicB[i])
                            logicOutput[(i * 2) + j] = (sbyte)(logicA[i] - 1);
                        else if (logicB[i] != 1)
                            logicOutput[(i * 2) + j] = (sbyte)(logicB[i] - 1);
                        else
                            logicOutput[(i * 2) + j] = (sbyte)(logicA[i] - 1);
                        break;

                    case 'F':
                    case 'U':
                    case ' ':
                        logicOutput[(i * 2) + j] = (sbyte)(((logicA[i] + logicB[i] + 2) % 3) - 1);
                        break;

                    case 'G':
                    case 'T':
                        logicOutput[(i * 2) + j] = (sbyte)((2 * Convert.ToByte(logicA[i] == logicB[i])) - 1);
                        break;

                    case 'H':
                    case 'S':
                        logicOutput[(i * 2) + j] = (sbyte)(Math.Max(logicA[i], logicB[i]) - 1);
                        break;

                    case 'I':
                    case 'R':
                        logicOutput[(i * 2) + j] = (sbyte)((2 - Math.Max(logicA[i], logicB[i])) - 1);
                        break;

                    case 'J':
                    case 'Q':
                        if (logicA[i] == 1 || logicB[i] == 1)
                            logicOutput[(i * 2) + j] = 0;
                        else if (logicA[i] == logicB[i])
                            logicOutput[(i * 2) + j] = 1;
                        else
                            logicOutput[(i * 2) + j] = -1;
                        break;

                    case 'K':
                    case 'P':
                        if (logicA[i] + logicB[i] == 2)
                            logicOutput[(i * 2) + j] = 0;
                        else if (logicA[i] + logicB[i] > 2)
                            logicOutput[(i * 2) + j] = 1;
                        else
                            logicOutput[(i * 2) + j] = -1;
                        break;

                    case 'L':
                    case 'O':
                        if (logicA[i] == 1)
                            logicOutput[(i * 2) + j] = 0;
                        else if (logicA[i] == logicB[i] || logicA[i] + logicB[i] == 3)
                            logicOutput[(i * 2) + j] = 1;
                        else
                            logicOutput[(i * 2) + j] = -1;
                        break;

                    case 'M':
                    case 'N':
                        logicOutput[(i * 2) + j] = (sbyte)(Mathf.Clamp(4 - (logicA[i] + logicB[i]), 0, 2) - 1);
                        break;
                }
            }

        sbyte offset = 0;
        for (byte i = 0; i < logicOutput.Length; i++)
            offset += logicOutput[i];

        string newScreen = "";
        for (byte i = 0; i < screen.Length; i++)
            newScreen += DigitalRoot((byte)(byte.Parse(screen[i].ToString()) + Math.Abs(offset)));

        Debug.LogFormat("[hexOS #{0}]: The output from each logic computation is {1}", _moduleId, logicOutput.Join(", "));
        Debug.LogFormat("[hexOS #{0}]: Combining all of them gives the offset {1}.", _moduleId, offset);
        Debug.LogFormat("[hexOS #{0}]: The modified screen display is {1}.", _moduleId, newScreen);

        return (short.Parse(ThreeDigit(newScreen)) % 1000).ToString();
    }

    /// <summary>
    /// An algorithm that takes a 30-digit number and compresses it to a 3- or 4-digit number to return as the answer of the module.
    /// </summary>
    /// <param name="seq">The sequence of digits that will be used.</param>
    private string ThreeDigit(string seq)
    {
        Debug.LogFormat("[hexOS #{0}]: Current sequence > {1}", _moduleId, seq);

        //create groups of 6
        List<int> digits = new List<int>(0);
        for (byte i = 5; i < seq.Length; i += 6)
            digits.Add(int.Parse(string.Concat(seq[i - 5], seq[i - 4], seq[i - 3], seq[i - 2], seq[i - 1], seq[i])));

        Debug.LogFormat("[hexOS #{0}]: Forming groups > {1}", _moduleId, digits.Join(", "));
        seq = "";

        //add groups of 6 with each other
        for (byte i = 0; i < digits.Count; i++)
            seq += (digits[i] / 1000 + digits[i] % 1000).ToString();

        Debug.LogFormat("[hexOS #{0}]: Combining the groups > {1}", _moduleId, seq);

        //get leftovers
        string leftover = "";
        for (byte i = (byte)(Math.Floor(seq.Length / 6f) * 6); i < seq.Length && i != 0; i++)
            leftover += seq[i];

        string newSeq = "";

        if (leftover.Length != 0)
        {
            //add leftovers to sequence
            for (byte i = 0; i < (Math.Floor(seq.Length / 6f) * 6); i++)
                newSeq += DigitalRoot((byte)(byte.Parse(seq[i].ToString()) + byte.Parse(leftover[i % leftover.Length].ToString()))).ToString();

            Debug.LogFormat("[hexOS #{0}]: Leftovers > {1}", _moduleId, leftover);
            Debug.LogFormat("[hexOS #{0}]: Modified sequence > {1}", _moduleId, newSeq);
        }

        else
        {
            Debug.LogFormat("[hexOS #{0}]: No leftovers. Continue as normal.", _moduleId);
            newSeq = seq;
        }

        //repeat if more than 4 digits long
        if (newSeq.Length > 4)
        {
            Debug.LogFormat("[hexOS #{0}]: Sequence is not 3-4 digits long. Repeat this process.", _moduleId);
            newSeq = ThreeDigit(newSeq);
        }

        return newSeq;
    }

    /// <summary>
    /// Calculates and returns the digital root of the number provided.
    /// </summary>
    /// <param name="num">The number that will be used to calculate the digital root of itself.</param>
    private byte DigitalRoot(byte num)
    {
        byte result = 0;

        //repeat until 1-digit number
        while (num > 9)
        {
            //take each number seperately and add them together
            foreach (char c in num.ToString())
                result += (byte)char.GetNumericValue(c);

            num = result;
        }

        return num;
    }

    /// <summary>
    /// Determines whether the input from the TwitchPlays chat command is valid or not.
    /// </summary>
    /// <param name="par">The string from the user.</param>
    private bool IsValid(string par)
    {
        //0-999
        ushort s;
        return ushort.TryParse(par, out s) && s < 1000;
    }

    private static readonly Color32[] _color32s = new Color32[3] { new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255), new Color32(255, 0, 255, 255) };

    private static readonly string[] _solveChords = new string[18] { "A2", "A3", "A4", "B2", "B3", "C2", "C3", "C4", "C5", "D3", "D4", "F2", "F3", "F4", "F5", "G2", "G3", "G4" };

    private static readonly string[] _notes = new string[16] { "X-XXX-X-X-X-X---X---X-X-X---X---X-X-X", "XXX-X-X-X-X-X---X---X-X-X-X---X---X-X",
                                                               "X-X-XXX-X-X-X---X---X-X-X-X---X-X-X-X", "XXXXX-X-X-X---X-X---X-X-X-X---X-X-X-X",
                                                               "X-XXX-X-X-X-X---X---X-X-X---X--XX-X-X", "XXX-X-X-X-X-X---X-XXX-X-X--XX-X---X-X",
                                                               "X-X-XXX-X-X-X---X--XX-X-X--X-X-XX-X-X", "XXXXX-X-X-X---X-X--XX-X-X--X--X--XX-X",
                                                               "X-XXX-X-X-X-X---X-XXX-X-X---X-XXX-X-X", "XXX-X-X-X-X-X--XX-X-X-X-X-XXX-X---X-X",
                                                               "X-X-XXX-X-X-X---X-XXX-X-X-XXX-X-X-X-X", "XXXXX-X-X-X---X-X--XX-X-X-X---X-X-XXX",
                                                               "X-XXX-X-X-X-X---X-XXX-XXX-X-X--XX-X-X", "XXX-X-X-X-X-X--XX-XXX-X-X--XX-X---X-X",
                                                               "X-X-XXX-X-X-X---X--XX-X-X--XXX-XX-X-X", "XXXXX-X-X-X---X-X--XX-X-X-XXXXX-X-X-X" };

    private static readonly string[] _solve = {
        "\"You solved this... manually?\nI... how...?\"",
        "\"Maybe solving this is a\nlot easier when your\nhands aren't slowed\ndown by air friction...\"",
        "\"Technology was a mistake.\nI'm sticking with magic.\"",
        "\"Who the hell is making\nthese modules anyway?\"",
        "\"Is this why I scare\npeople off? For indirectly\ncreating something\nlike this?\"",
        "\"I'm glad you made\nit through that\nwith neither you, nor\nthe fragments getting\nturned into scraps...\"",
        "\"I thought toys were\nsupposed to be safe...\"",
        "\"I'm going back to my\nrhythm games, at least there,\nI don't have to think...\"",
        "\"...I am going to throw\nmyself into the sea.\nI will float there until\nI am ready to go home.\"",
        "\"Now I remember why we\ndon't combine technology\nwith magic...\"",
    };

    private static readonly string[] _solveAlt = {
        "YOU FOUND IT!",
        "Dungeon: hexOS Vaults\nSolver\ncleared the dungeon.",
        "Break!!!",
        "Complete!!!",
        "oh yeah woo yeah",
        "NOOO I DON'T WANNA\nBE A MODULE\nbeing a module is\nfine actually,\ni don't care anymore",
        "construct music",
        "IN A DESPERATE CONFLICT\nWITH A RUTHLESS ENEMY",
        "the fuck is a rotom",
        "hexyl is a faggot.\nim going to go play sims",
    };

#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"!{0} play (Plays the sequence provided by the module.) - !{0} submit <###> (Submits the number by holding the button at those specific times. | Valid numbers range from 0-999 | Example: !{0} submit 420)";
#pragma warning restore 414

    /// <summary>
    /// TwitchPlays Compatibility, detects every chat message and clicks buttons accordingly.
    /// </summary>
    /// <param name="command">The twitch command made by the user.</param>
    IEnumerator ProcessTwitchCommand(string command)
    {
        //splits each command by spaces
        string[] buttonPress = command.Split(' ');

        //if command is formatted correctly
        if (Regex.IsMatch(buttonPress[0], @"^\s*play\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return null;

            //no number
            if (buttonPress.Length != 1)
                yield return "sendtochaterror This command has no parameters! Use !{0} to submit a number!";

            //sequence is already playing
            if (_playSequence)
                yield return "sendtochaterror The sequence is already being played! Wait until the sequence is over!";

            //this command is valid, play sequence
            else
            {
                Button.OnInteract();
                Button.OnInteractEnded();
            }
        }

        //if command is formatted correctly
        else if (Regex.IsMatch(buttonPress[0], @"^\s*submit\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return null;

            //no number
            if (buttonPress.Length < 2)
                yield return "sendtochaterror A number must be specified! (Valid: 0-999)";

            //more than one number
            else if (buttonPress.Length > 2)
                yield return "sendtochaterror Only one number must be specified! (Valid: 0-999)";

            //number outside range
            else if (!IsValid(buttonPress.ElementAt(1)))
                yield return "sendtochaterror Number wasn't in range! (Valid: 0-999)";

            //if command is valid, push button accordingly
            else
            {
                //add leading 0's
                while (buttonPress[1].Length < 3)
                    buttonPress[1] = "0" + buttonPress[1];

                //will quickly determine if the module is about to solve or strike
                if (buttonPress[1] == _answer)
                    yield return "solve";

                else
                    yield return "strike";

                //cycle through each digit
                for (byte i = 0; i < buttonPress[1].Length; i++)
                {
                    //wait until the correct number is shown
                    yield return new WaitWhile(() => buttonPress[1][i] != Number.text[i]);

                    //hold button
                    Button.OnInteract();

                    //wait until module can submit
                    yield return new WaitWhile(() => _held < 20);

                    //release button
                    Button.OnInteractEnded();
                }
            }
        }
    }

    /// <summary>
    /// Force the module to be solved in TwitchPlays
    /// </summary>
    IEnumerator TwitchHandleForcedSolve()
    {
        yield return null;

        //get the correct answer
        string solve = _answer;

        Debug.LogFormat("[hexOS #{0}]: Admin has initiated autosolver. The module will now submit {1}.", _moduleId, solve);

        //cycle through each digit
        for (byte i = 0; i < solve.Length; i++)
        {
            //wait until the correct number is shown
            yield return new WaitWhile(() => solve[i] != Number.text[i]);

            //hold button
            Button.OnInteract();

            //wait until module can submit
            yield return new WaitWhile(() => _held < 20);

            //release button
            Button.OnInteractEnded();
        }
    }
}