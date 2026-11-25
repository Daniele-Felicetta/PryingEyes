#if !PE_DISTRIBUTION_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using PryingEyes.Proc;

namespace PryingEyes.Testing
{
    /// <summary>The base of an in-player test: a component that installs itself when its flag is on the command line, runs a sequence of
    /// checks against the real game and ends the process with 0 (every check passed), 1, or 2 for a plan that cannot host it (<see cref="NotApplicable"/>).
    /// It owns what each test used to copy: counting
    /// the checks and the failures, counting the exceptions, driving the sequence, the result line, <c>result.json</c> and the exit.
    /// A test says its name, writes its checks in <see cref="Run"/> and installs itself with one line in its own load hook:
    /// <code>
    /// [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    /// static void Install() { InstallWhen&lt;TrapTest&gt;("-traptest"); }
    /// </code>
    /// The result line is the one every script already reads: <c>[PryingEyes] traptest RISULTATO SUPERATA: 18/18</c>. Compiled out of
    /// distribution builds, and named by no file that is not a test (the guard, <c>tools/check_suite.py</c>, fails on a reference).
    /// Design: docs/engineering/agent-tooling.md, "The in-player harness".</summary>
    public abstract class PlayerTest : MonoBehaviour
    {
        struct Outcome { public string Name; public bool Ok; public string Detail; }

        readonly List<Outcome> results = new List<Outcome>();
        readonly List<string> artifacts = new List<string>();
        int failed, exceptions;
        bool photos, ended;
        float started;
        string outDir;
        string inapplicable;

        /// <summary>The name on the result line and in <c>result.json</c>, and the prefix of every check in the log ("traptest").</summary>
        protected abstract string Suite { get; }

        /// <summary>The checks, in order. An exception ends the sequence, is logged and fails the check "sequenza completa".</summary>
        protected abstract IEnumerator Run();

        /// <summary>The flag that asks for photographs ("-trapshots"); null for a test that takes none.</summary>
        protected virtual string PhotoFlag { get { return null; } }

        /// <summary>The folder under <c>unity_logs/</c> that this suite writes into when no <c>-testout</c> names one: its photographs
        /// and its <c>result.json</c>. By default the name of the suite; a test keeps the folder its page has always named.</summary>
        protected virtual string OutFolder { get { return Suite; } }

        /// <summary>Called when the sequence has ended, however it ended and before the last check. For what a test must put back.</summary>
        protected virtual void Finish() { }

        /// <summary>The seconds, counted from the start of the player, within which the whole run must end; 0 (the default) for no limit. Past it
        /// the base fails the check "finita entro il tempo" and ends the run as it ends any other, whatever the sequence is still waiting for.</summary>
        protected virtual float TimeLimit { get { return 0f; } }

        /// <summary>Whether the bot of the automatic run must not shoot by itself while this test runs: a shot wakes the whole floor, and a test that
        /// measures what a weapon does wants the weapon to itself. The game reads it through <see cref="TestRun.HoldFire"/>, set before the first frame.</summary>
        protected virtual bool HoldFire { get { return false; } }

        /// <summary>For a test that two processes run together (the host and the friend of a co-op race): who speaks in this one. It follows the
        /// name of the suite in the log, in every check and in the result line ("pistacooptest ospite ok ..."), and nowhere else: the test names
        /// its <see cref="OutFolder"/> so that the processes do not write each other's files. Null for the others.</summary>
        protected virtual string Role { get { return null; } }

        /// <summary>The name of the check that closes the sequence ("nessuna eccezione runtime"); null for a suite that has never made one,
        /// which then keeps the checks it always had. The exceptions are counted either way and <c>result.json</c> carries the number.</summary>
        protected virtual string ExceptionCheck { get { return "nessuna eccezione runtime"; } }

        /// <summary>Whether a line of the game's log is an error that fails the closing check: an exception, unless a suite widens it
        /// to something its subject logs as a warning.</summary>
        protected virtual bool IsError(string message, LogType type) { return type == LogType.Exception; }

        /// <summary>The checks made so far, and how many of them failed.</summary>
        protected int Checks { get { return results.Count; } }
        protected int Failed { get { return failed; } }

        /// <summary>Whether the photo flag is on: a test waits for its picture only then.</summary>
        protected bool Photos { get { return photos; } }

        /// <summary>One check. The name is what the log and <c>result.json</c> show; <paramref name="detail"/> is what was observed against
        /// what was expected, when the name does not say it. Returns <paramref name="ok"/>, so a test can stop on a failure.</summary>
        protected bool Check(string name, bool ok, string detail = "")
        {
            results.Add(new Outcome { Name = name, Ok = ok, Detail = detail ?? "" });
            if (!ok) failed++;
            Debug.Log("[PryingEyes] " + Label + " " + (ok ? "ok " : "FALLITO ") + name + (string.IsNullOrEmpty(detail) ? "" : " (" + detail + ")"));
            return ok;
        }

        /// <summary>The floor or the seed that this run was given cannot host the test (no zone to seal, no free spot, no sanctuary): say why and return
        /// from <see cref="Run"/>. The check "il piano ospita la prova" fails with the reason, so every reader sees a run that proved nothing, and the
        /// process quits with 2 instead of 1: a script reads 2 as "change the seed" and 1 as "the game broke". A check that failed for another reason
        /// outranks it (exit 1): the plan is only the explanation when nothing else went wrong.</summary>
        protected void NotApplicable(string why)
        {
            inapplicable = why ?? "";
            Check("il piano ospita la prova", false, inapplicable);
        }

        /// <summary>Ends the run here and now, from inside the sequence, as the end of the sequence does (the clean-up, the closing check, the result and the exit
        /// request), and returns: the sequence must return right after. For a test whose process has to leave the game before the others do (the co-op friend that
        /// goes away, or is killed by itself once its result is written). A second end, by the sequence or by the clock, is refused.</summary>
        protected void EndNow() { End(); }

        /// <summary>A photograph of what the player sees, HUD included, into the output folder. Nothing happens unless the photo flag is on.</summary>
        protected void Photo(string name)
        {
            if (!photos) return;
            LightPool.Tick(false, Camera.main.transform.position);
            GameManager.CamShot(PhotoPath(name), null, true);
        }

        /// <summary>The file of a photograph that the test frames itself (a weapon hidden, a review camera, no HUD): in the output folder,
        /// and listed in <c>result.json</c> as an artifact. <see cref="Photo"/> is the plain case and uses it. A test whose photographs are optional
        /// asks only when <see cref="Photos"/> is on; one that always takes them asks every time.</summary>
        protected string PhotoPath(string name)
        {
            string file = name + ".png";
            artifacts.Add(file);
            return Path.Combine(OutPath, file);
        }

        /// <summary>The line of a test's load hook: when <paramref name="flag"/> is on the command line, say that a test owns the process
        /// (<see cref="TestRun.Dedicated"/>, and <see cref="TestRun.HoldFire"/> when the test asks for it) and start the test.</summary>
        protected static void InstallWhen<T>(string flag) where T : PlayerTest
        {
            if (!TestRun.Has(flag)) return;
            TestRun.Dedicated = true;
            var test = new GameObject(typeof(T).Name).AddComponent<T>();
            if (test.HoldFire) TestRun.HoldFire = true;
        }

        string OutPath { get { return outDir ?? (outDir = TestRun.OutDir(OutFolder)); } }

        string Label { get { return Role == null ? Suite : Suite + " " + Role; } }

        void OnLog(string message, string stack, LogType type) { if (IsError(message, type)) exceptions++; }

        /// <summary>Stops counting exceptions. A test that owns something to release overrides it and calls <c>base.OnDestroy()</c>: Unity calls
        /// only the most derived <c>OnDestroy</c>, so a second one that does not override this one would silence it.</summary>
        protected virtual void OnDestroy() { Application.logMessageReceived -= OnLog; }

        IEnumerator Start()
        {
            Application.logMessageReceived += OnLog;
            started = Time.realtimeSinceStartup;
            photos = PhotoFlag != null && TestRun.Has(PhotoFlag);
            if (TimeLimit > 0f) StartCoroutine(Watch());
            var sequence = Run();
            while (true)
            {
                bool more; try { more = sequence.MoveNext(); } catch (Exception e) { Debug.LogException(e); Check("sequenza completa", false); break; }
                if (!more) break; yield return sequence.Current;
            }
            End();
        }

        /// <summary>The end of the run, once however it comes: the test's own clean-up, the closing check, the result and the exit.</summary>
        void End()
        {
            if (ended) return;
            ended = true;
            Finish();
            if (ExceptionCheck != null) Check(ExceptionCheck, exceptions == 0);
            WriteResult();
            Debug.Log("[PryingEyes] " + Label + " RISULTATO " + (failed == 0 ? "SUPERATA" : "FALLITA") + ": " + (results.Count - failed) + "/" + results.Count);
            Application.Quit(failed == 0 ? 0 : inapplicable != null && failed == 1 ? 2 : 1);
        }

        /// <summary>Watches <see cref="TimeLimit"/> every frame in a coroutine of its own, so that a sequence stuck inside a wait still ends.</summary>
        IEnumerator Watch()
        {
            while (!ended)
            {
                if (Time.realtimeSinceStartup > TimeLimit)
                {
                    Check("finita entro il tempo", false, TimeLimit.ToString("F0", CultureInfo.InvariantCulture) + " s");
                    End();
                    yield break;
                }
                yield return null;
            }
        }

        /// <summary>The result as data, for the tools that start a suite and need no regular expression: docs/engineering/agent-tooling.md,
        /// "Results as data". A failure to write it is a warning: it never changes the verdict or the exit code.</summary>
        void WriteResult()
        {
            try
            {
                var json = new StringBuilder(2048);
                json.Append("{\n  \"schemaVersion\": 1,\n  \"suite\": "); Quote(json, Suite);
                json.Append(",\n  \"ok\": ").Append(failed == 0 ? "true" : "false");
                json.Append(",\n  \"checks\": [");
                for (int i = 0; i < results.Count; i++)
                {
                    json.Append(i == 0 ? "\n    " : ",\n    ").Append("{\"name\": "); Quote(json, results[i].Name);
                    json.Append(", \"ok\": ").Append(results[i].Ok ? "true" : "false");
                    if (results[i].Detail.Length > 0) { json.Append(", \"detail\": "); Quote(json, results[i].Detail); }
                    json.Append('}');
                }
                json.Append("\n  ],\n  \"exceptions\": ").Append(exceptions.ToString(CultureInfo.InvariantCulture));
                json.Append(",\n  \"seconds\": ").Append((Time.realtimeSinceStartup - started).ToString("0.0", CultureInfo.InvariantCulture));
                json.Append(",\n  \"version\": "); Quote(json, Application.version);
                json.Append(",\n  \"args\": [");
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 1; i < args.Length; i++)           // not the executable; and no path of this machine in what a runner keeps
                {
                    if (i > 1) json.Append(", ");
                    Quote(json, IsPath(args[i]) ? "<path>" : args[i]);
                }
                json.Append("],\n  \"artifacts\": [");
                for (int i = 0; i < artifacts.Count; i++) { if (i > 0) json.Append(", "); Quote(json, artifacts[i]); }
                json.Append("]\n}\n");
                File.WriteAllText(Path.Combine(OutPath, "result.json"), json.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning("[PryingEyes] " + Suite + " result.json non scritto: " + e.Message); }
        }

        static bool IsPath(string word)
        {
            try { return Path.IsPathRooted(word); } catch (ArgumentException) { return false; }      // a word with characters no path may hold is not one
        }

        /// <summary>A JSON string: quotes, backslashes and control characters escaped, every character outside printable ASCII written as
        /// \uXXXX, so that the file is plain ASCII and reads the same in any tool and any encoding.</summary>
        static void Quote(StringBuilder json, string text)
        {
            json.Append('"');
            foreach (char c in text ?? "")
            {
                switch (c)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c > 0x7e) json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else json.Append(c);
                        break;
                }
            }
            json.Append('"');
        }
    }
}
#endif
