using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace MrTGame.Core
{
    public enum GameAutosaveReadSource { None, Main, Backup, Temporary }

    public sealed class GameAutosaveReadResult
    {
        public bool Success { get; internal set; }
        public string Payload { get; internal set; }
        public string Error { get; internal set; }
        public string RecoveryReason { get; internal set; }
        public bool FoundFiles { get; internal set; }
        public bool IsFutureVersion { get; internal set; }
        public GameAutosaveReadSource Source { get; internal set; }
        public bool RecoveredFromBackup { get { return Source == GameAutosaveReadSource.Backup; } }
        public bool RecoveredFromTemporary { get { return Source == GameAutosaveReadSource.Temporary; } }
    }

    public sealed class GameAutosaveWriteResult
    {
        public bool Success { get; internal set; }
        public string Error { get; internal set; }
        public bool IsFutureVersion { get; internal set; }
    }

    /// <summary>
    /// One autosave and its recovery copy. Payload serialization stays on the caller's
    /// thread; this class uses no Unity APIs and may write on a worker thread.
    /// </summary>
    public sealed class GameAutosaveStorage
    {
        public const int CurrentVersion = 1;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static readonly object PathLocksGate = new object();
        static readonly Dictionary<string, object> PathLocks = new Dictionary<string, object>(
            Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        readonly object gate;
        readonly string directory;
        bool isFutureVersion;

        public string MainPath { get; private set; }
        public string BackupPath { get; private set; }
        public string TemporaryPath { get; private set; }
        public bool IsFutureVersion { get { lock (gate) return isFutureVersion; } }
        public bool HasValidSave { get { return Read().Success; } }

        public GameAutosaveStorage(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A save directory is required.", "directory");
            this.directory = Path.GetFullPath(directory);
            MainPath = Path.Combine(this.directory, "autosave.json");
            BackupPath = MainPath + ".bak";
            TemporaryPath = MainPath + ".tmp";
            lock (PathLocksGate)
            {
                if (!PathLocks.TryGetValue(MainPath, out gate))
                {
                    gate = new object();
                    PathLocks.Add(MainPath, gate);
                }
            }
        }

        public GameAutosaveReadResult Read()
        {
            lock (gate)
            {
                Candidate main = ReadCandidate(MainPath);
                Candidate backup = ReadCandidate(BackupPath);
                Candidate temporary = ReadCandidate(TemporaryPath);
                Candidate future = FirstFuture(main, backup, temporary);
                isFutureVersion = future != null;
                // Never silently roll a newer game's save back to an older backup.
                if (future != null)
                    return new GameAutosaveReadResult { FoundFiles = true, IsFutureVersion = true, Error = future.Error };

                if (main.Status == CandidateStatus.Valid) return Success(main, GameAutosaveReadSource.Main, null);
                if (backup.Status == CandidateStatus.Valid) return Success(backup, GameAutosaveReadSource.Backup, main.Error);
                if (temporary.Status == CandidateStatus.Valid) return Success(temporary, GameAutosaveReadSource.Temporary, main.Error);

                bool found = main.Status != CandidateStatus.Missing || backup.Status != CandidateStatus.Missing || temporary.Status != CandidateStatus.Missing;
                return new GameAutosaveReadResult
                {
                    FoundFiles = found,
                    Error = found ? JoinErrors(main, backup, temporary) : null
                };
            }
        }

        public GameAutosaveWriteResult Write(string payload)
        {
            lock (gate)
            {
                if (string.IsNullOrWhiteSpace(payload)) return WriteFailure("The autosave payload is empty.");
                try
                {
                    Candidate main = ReadCandidate(MainPath);
                    Candidate backup = ReadCandidate(BackupPath);
                    Candidate temporary = ReadCandidate(TemporaryPath);
                    Candidate future = FirstFuture(main, backup, temporary);
                    isFutureVersion = future != null;
                    if (future != null) return WriteFailure(future.Error, true);
                    // An unreadable file may belong to a newer version. Do not guess.
                    if (main.Status == CandidateStatus.Unreadable) return WriteFailure(main.Error);
                    if (backup.Status == CandidateStatus.Unreadable) return WriteFailure(backup.Error);
                    if (temporary.Status == CandidateStatus.Unreadable) return WriteFailure(temporary.Error);

                    byte[] bytes = Encode(payload);
                    Directory.CreateDirectory(directory);

                    // If an interrupted first save is the only surviving good copy,
                    // promote it before reusing the temporary path.
                    if (main.Status != CandidateStatus.Valid && backup.Status != CandidateStatus.Valid && temporary.Status == CandidateStatus.Valid)
                    {
                        if (main.Status == CandidateStatus.Missing) File.Move(TemporaryPath, MainPath);
                        else File.Replace(TemporaryPath, MainPath, null);
                        main = ReadCandidate(MainPath);
                        if (main.Status != CandidateStatus.Valid) return WriteFailure("Could not preserve the recovered temporary autosave.");
                    }

                    using (FileStream file = new FileStream(TemporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        file.Write(bytes, 0, bytes.Length);
                        file.Flush(true);
                    }

                    if (main.Status == CandidateStatus.Missing)
                        File.Move(TemporaryPath, MainPath);
                    else
                        // A damaged primary must not replace the last healthy backup.
                        // File.Replace failure leaves the original in place; never
                        // emulate it with Delete + Move.
                        File.Replace(TemporaryPath, MainPath, main.Status == CandidateStatus.Valid ? BackupPath : null);

                    return new GameAutosaveWriteResult { Success = true };
                }
                catch (Exception exception) when (IsFileOrDataException(exception))
                {
                    return WriteFailure("Autosave write failed: " + exception.Message);
                }
            }
        }

        static GameAutosaveWriteResult WriteFailure(string error, bool future = false)
        {
            return new GameAutosaveWriteResult { Error = error, IsFutureVersion = future };
        }

        static GameAutosaveReadResult Success(Candidate candidate, GameAutosaveReadSource source, string reason)
        {
            return new GameAutosaveReadResult { Success = true, FoundFiles = true, Payload = candidate.Payload, Source = source, RecoveryReason = reason };
        }

        static Candidate FirstFuture(params Candidate[] candidates)
        {
            foreach (Candidate candidate in candidates)
                if (candidate.Status == CandidateStatus.Future) return candidate;
            return null;
        }

        static string JoinErrors(params Candidate[] candidates)
        {
            List<string> errors = new List<string>();
            foreach (Candidate candidate in candidates)
                if (candidate.Status != CandidateStatus.Missing && !string.IsNullOrEmpty(candidate.Error)) errors.Add(candidate.Error);
            return string.Join(" | ", errors.ToArray());
        }

        static Candidate ReadCandidate(string path)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (FileNotFoundException) { return new Candidate { Status = CandidateStatus.Missing }; }
            catch (DirectoryNotFoundException) { return new Candidate { Status = CandidateStatus.Missing }; }
            catch (Exception exception) when (IsFileOrDataException(exception))
            {
                return new Candidate { Status = CandidateStatus.Unreadable, Error = Path.GetFileName(path) + ": " + exception.Message };
            }

            try
            {
                // Read only version first: a future envelope can change the payload
                // schema and checksum without being mistaken for corruption.
                EnvelopeVersion header;
                using (MemoryStream stream = new MemoryStream(bytes, false))
                    header = (EnvelopeVersion)new DataContractJsonSerializer(typeof(EnvelopeVersion)).ReadObject(stream);
                if (header == null) throw new SerializationException("The envelope is null.");
                if (header.Version > CurrentVersion)
                    return new Candidate { Status = CandidateStatus.Future, Error = Path.GetFileName(path) + " uses autosave version " + header.Version + "; this game supports version " + CurrentVersion + "." };
                if (header.Version != CurrentVersion) throw new SerializationException("Unsupported or missing autosave version.");

                Envelope envelope;
                using (MemoryStream stream = new MemoryStream(bytes, false))
                    envelope = (Envelope)new DataContractJsonSerializer(typeof(Envelope)).ReadObject(stream);
                if (envelope == null || string.IsNullOrWhiteSpace(envelope.Payload)) throw new SerializationException("The autosave payload is missing.");
                if (envelope.Sha256 == null || envelope.Sha256.Length != 64 || !string.Equals(envelope.Sha256, Hash(envelope.Payload), StringComparison.OrdinalIgnoreCase))
                    throw new SerializationException("The autosave checksum does not match.");
                return new Candidate { Status = CandidateStatus.Valid, Payload = envelope.Payload };
            }
            catch (Exception exception) when (IsFileOrDataException(exception))
            {
                return new Candidate { Status = CandidateStatus.Invalid, Error = Path.GetFileName(path) + ": " + exception.Message };
            }
        }

        static byte[] Encode(string payload)
        {
            Envelope envelope = new Envelope { Version = CurrentVersion, Payload = payload, Sha256 = Hash(payload) };
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(Envelope)).WriteObject(stream, envelope);
                return stream.ToArray();
            }
        }

        static string Hash(string payload)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Utf8.GetBytes(payload))).Replace("-", string.Empty);
        }

        static bool IsFileOrDataException(Exception exception)
        {
            return exception is IOException || exception is UnauthorizedAccessException || exception is SerializationException
                || exception is System.Xml.XmlException || exception is ArgumentException || exception is NotSupportedException
                || exception is System.Security.SecurityException || exception is CryptographicException;
        }

        enum CandidateStatus { Missing, Valid, Invalid, Future, Unreadable }
        sealed class Candidate
        {
            public CandidateStatus Status;
            public string Payload;
            public string Error;
        }

        [DataContract]
        sealed class EnvelopeVersion
        {
            [DataMember(Name = "version", Order = 0)] public int Version = 0;
        }

        [DataContract]
        sealed class Envelope
        {
            [DataMember(Name = "version", Order = 0)] public int Version;
            [DataMember(Name = "payload", Order = 1)] public string Payload;
            [DataMember(Name = "sha256", Order = 2)] public string Sha256;
        }
    }
}
