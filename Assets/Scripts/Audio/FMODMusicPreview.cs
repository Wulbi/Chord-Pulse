using System;
using System.Collections;
using FMODUnity;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class FMODMusicPreview : MonoBehaviour
{
    const string BankName = "ChordPulse";
    const string EventPath = "event:/Music/ChordPulsePreview";

    FMOD.Studio.EventInstance _music;
    bool _ownsBank;
    bool _ready;
    bool _playing;
    bool _requestedFever;
    string _error;

    IEnumerator Start()
    {
        try
        {
            _ownsBank = !RuntimeManager.HasBankLoaded(BankName);
            if (_ownsBank)
                RuntimeManager.LoadBank(BankName, true);
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }

        if (_error != null)
            yield break;

        FMOD.Studio.Bank bank;
        if (!Check(RuntimeManager.StudioSystem.getBank("bank:/" + BankName, out bank)))
            yield break;
        if (!Check(bank.loadSampleData()))
            yield break;

        while (true)
        {
            FMOD.Studio.LOADING_STATE state;
            if (!Check(bank.getSampleLoadingState(out state)))
                yield break;
            if (state == FMOD.Studio.LOADING_STATE.LOADED)
                break;
            if (state == FMOD.Studio.LOADING_STATE.ERROR)
            {
                Fail("FMOD sample data failed to load.");
                yield break;
            }
            yield return null;
        }

        try
        {
            _music = RuntimeManager.CreateInstance(EventPath);
            _ready = true;
            Play();
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (!_ready || keyboard == null)
            return;
        if (keyboard.fKey.wasPressedThisFrame)
            ToggleFever();
        if (keyboard.spaceKey.wasPressedThisFrame)
            TogglePlayback();
    }

    void Play()
    {
        _requestedFever = false;
        _playing = Check(_music.setParameterByName("Fever", 0f)) && Check(_music.start());
    }

    void ToggleFever()
    {
        bool next = !_requestedFever;
        if (Check(_music.setParameterByName("Fever", next ? 1f : 0f)))
            _requestedFever = next;
    }

    void TogglePlayback()
    {
        if (_playing)
        {
            Check(_music.stop(FMOD.Studio.STOP_MODE.IMMEDIATE));
            _playing = false;
        }
        else
            Play();
    }

    bool Check(FMOD.RESULT result)
    {
        if (result == FMOD.RESULT.OK)
            return true;
        Fail(result.ToString());
        return false;
    }

    void Fail(string message)
    {
        _error = message;
        Debug.LogError("[FMOD Music Preview] " + message, this);
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(30, 30, 640, 300));
        GUILayout.Label("CHORD PULSE / FMOD SAMPLE PREVIEW");
        GUILayout.Label("120 BPM / C - Am - F - G / 4 bars per section");
        if (_error != null)
            GUILayout.Label(_error);
        else if (!_ready)
            GUILayout.Label("Loading samples...");
        else
        {
            int milliseconds;
            _music.getTimelinePosition(out milliseconds);
            bool fever = milliseconds >= 8000;
            GUILayout.Label("Playing section: " + (fever ? "FEVER" : "NORMAL"));
            GUILayout.Label("Requested section: " + (_requestedFever ? "FEVER" : "NORMAL") + " (next bar)");
            GUILayout.Label("F changes section / Space stops or restarts");
            if (GUILayout.Button("Fever / Normal", GUILayout.Height(40)))
                ToggleFever();
            if (GUILayout.Button(_playing ? "Stop" : "Play", GUILayout.Height(40)))
                TogglePlayback();
        }
        GUILayout.EndArea();
    }

    void OnDestroy()
    {
        if (_music.isValid())
        {
            _music.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            _music.release();
            _music.clearHandle();
        }
        if (_ownsBank)
            RuntimeManager.UnloadBank(BankName);
    }
}
