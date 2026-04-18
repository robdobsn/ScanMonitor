# File Hash & Relocation System — Design Document

## Problem Statement

Over the life of a document filing system, files move between folders, folders get renamed, and directory structures are reorganised. The system needs to:

1. **Track** where filed documents are stored
2. **Detect** when files have moved from their recorded locations
3. **Relocate** them automatically by matching file content hashes against a current filesystem index

The existing ScanMonitor implementation has partial infrastructure for this but lacks automatic relocation. This document describes a complete redesign.

---

## Architecture Overview

```
┌─────────────────────┐     ┌──────────────────────┐
│  File Processing     │     │  Filesystem Indexer   │
│  (scan/ingest)       │     │  (periodic crawl)     │
│                      │     │                       │
│  - extract text      │     │  - walk filing trees   │
│  - generate images   │     │  - hash each file      │
│  - compute hash ←────┤     │  - upsert to DB        │
│  - store in DB       │     │                       │
└──────────┬───────────┘     └──────────┬────────────┘
		   │                            │
		   ▼                            ▼
┌──────────────────────────────────────────────────────┐
│                    Database                           │
│                                                       │
│  filed_documents:  {uniq_name, filed_path, file_hash} │
│  filesystem_index: {path, file_hash, file_size, date} │
└──────────────────────────────────────────────────────┘
		   │
		   ▼
┌─────────────────────┐
│  Relocation Engine   │
│                      │
│  - find broken paths │
│  - match by hash     │
│  - update records    │
│  - generate report   │
└──────────────────────┘
```

---

## Data Model

### `filed_documents` (one record per filed document)

| Field | Type | Notes |
|---|---|---|
| `uniq_name` | `TEXT PK` | Unique document identifier (e.g. `2026_04_15_09_08_14`) |
| `filed_path` | `TEXT` | Full path where the document was filed |
| `file_hash` | `BYTEA / BINARY` | Content hash computed at filing time |
| `file_size` | `BIGINT` | File size at filing time |
| `filed_at` | `TIMESTAMP` | When the document was filed |
| `doc_type` | `TEXT` | Document type classification |
| `hash_algorithm` | `TEXT` | e.g. `blake3` or `sha256` — allows future migration |
| `path_verified_at` | `TIMESTAMP NULL` | Last time the path was confirmed to exist |
| `path_status` | `TEXT` | `verified`, `missing`, `relocated`, `ambiguous` |

### `filesystem_index` (one record per file on disk)

| Field | Type | Notes |
|---|---|---|
| `id` | `SERIAL / BIGINT` | Auto-increment PK |
| `file_path` | `TEXT` | Full file path |
| `file_hash` | `BYTEA / BINARY` | Content hash |
| `file_size` | `BIGINT` | Size in bytes |
| `last_modified` | `TIMESTAMP` | File modification time |
| `indexed_at` | `TIMESTAMP` | When this record was created/updated |

**Index:** `CREATE INDEX idx_fs_hash ON filesystem_index (file_hash);`

### Database Compatibility

For dual MongoDB/Postgres support, use a repository abstraction:

```
trait DocumentStore {
	fn upsert_filed_doc(doc: &FiledDocument) -> Result<()>;
	fn get_filed_docs_with_missing_paths() -> Result<Vec<FiledDocument>>;
	fn upsert_fs_index_entry(entry: &FsIndexEntry) -> Result<()>;
	fn find_by_hash(hash: &[u8]) -> Result<Vec<FsIndexEntry>>;
	fn clear_fs_index() -> Result<()>;
}
```

Implement `MongoDocumentStore` and `PostgresDocumentStore` behind this trait. The relocation engine and indexer consume only the trait.

---

## Hash Algorithm

### Recommendation: BLAKE3

| Algorithm | Speed (large file) | Speed (small file) | Collision resistance |
|---|---|---|---|
| MD5 | ~500 MB/s | Fast | Broken — not recommended |
| SHA-256 | ~300 MB/s | Moderate | Good |
| **BLAKE3** | **~5 GB/s** | **Very fast** | **Excellent** |

BLAKE3 is the clear choice for a filesystem indexer:
- Significantly faster than SHA-256, especially on modern CPUs (uses SIMD)
- Cryptographically secure (unlike MD5)
- Rust-native (`blake3` crate is the reference implementation)
- Parallelisable internally for large files

### Metadata-Exclusion Hashing

The existing system uses `GenHashOnFileExcludingMetadata` which strips PDF metadata before hashing, so that re-dated or re-tagged PDFs still match. This is important and should be preserved.

**Approach:**
- For PDF files: parse with a PDF library, hash only the page content streams and embedded images (skip `/Info`, `/Metadata`, `/ID` dictionaries)
- For image files (JPG/PNG): hash raw pixel data or the file content minus EXIF headers
- For other files: hash the full file content
- Store the `hash_algorithm` field as e.g. `blake3-pdf-content` or `blake3-full` to distinguish

### When to Hash

**At filing time (critical — currently missing):**

In the current `ScanDocHandler.ProcessPdfFile` / `CopyAndMoveTheFile` flow, after the file is successfully copied to its destination, compute the hash and store it in the `FiledDocInfo` record. This is the single most important improvement — it means every newly filed document has a hash from day one.

```
// Pseudocode for the existing C# filing path:
// In CopyAndMoveTheFile, after successful copy:
byte[] fileHash = ComputeContentHash(fdi.filedAs_pathAndFileName);
fdi.fileHash = fileHash;
fdi.hashAlgorithm = "blake3-pdf-content";  // or appropriate variant
AddOrUpdateFiledDocRecInDb(fdi);
```

**At index time (periodic rebuild):**

The filesystem indexer crawls all filing folders and builds the `filesystem_index` table. This replaces the current "Recompute MD5" function.

---

## Rust Implementation

### Why Rust

- **Speed**: BLAKE3 + parallel file I/O = 10-100x faster than the current C#/MD5 approach
- **Memory safety**: no GC pauses during large crawls
- **Cross-platform**: runs on Windows and Linux (useful if the server moves)
- **CLI-first**: easy to run as a scheduled task or from any UI

### Crate Dependencies

```toml
[dependencies]
blake3 = "1"
walkdir = "2"             # recursive directory walking
rayon = "1"               # parallel file processing
sqlx = { version = "0.8", features = ["runtime-tokio", "postgres"] }  # or
mongodb = "3"             # MongoDB driver
tokio = { version = "1", features = ["full"] }
clap = { version = "4", features = ["derive"] }  # CLI argument parsing
serde = { version = "1", features = ["derive"] }
lopdf = "0.34"            # PDF parsing for metadata-excluded hashing
indicatif = "0.17"        # progress bars
```

### Proposed CLI Interface

```bash
# Build the filesystem index (replaces "Recompute MD5")
scan-relocator index --folders "\\macallan\Main\RobAndJudyPersonal;\\macallan\Main\Rob Business" \
					 --db postgres://localhost/scanmanager

# Check which filed documents have moved
scan-relocator check --db postgres://localhost/scanmanager

# Automatically relocate files that have a single unambiguous hash match
scan-relocator relocate --db postgres://localhost/scanmanager \
						--dry-run          # preview changes
scan-relocator relocate --db postgres://localhost/scanmanager \
						--apply            # actually update records
						--backup-json relocated_2026-04-16.json  # save old paths

# Compute and store hash for a single file (called from the filing pipeline)
scan-relocator hash-file --file "\\macallan\Main\...\doc.pdf" \
						 --uniq-name 2026_04_15_09_08_14 \
						 --db postgres://localhost/scanmanager
```

### Module Structure

```
src/
├── main.rs              # CLI entry point (clap)
├── config.rs            # DB connection, folder paths
├── hash.rs              # BLAKE3 hashing, PDF content extraction
├── indexer.rs           # Filesystem walker + parallel hashing
├── checker.rs           # Compare filed_documents paths vs reality
├── relocator.rs         # Match missing files by hash, update DB
├── db/
│   ├── mod.rs           # DocumentStore trait
│   ├── postgres.rs      # Postgres implementation
│   └── mongo.rs         # MongoDB implementation
└── models.rs            # FiledDocument, FsIndexEntry structs
```

---

## Relocation Algorithm

```
FUNCTION relocate_missing_files():
	missing = db.get_filed_docs_where(path_status = 'missing'
									  OR file_at_path_does_not_exist)

	FOR each doc IN missing:
		candidates = db.find_fs_index_by_hash(doc.file_hash)

		IF candidates.len() == 0:
			doc.path_status = 'missing'        # truly gone
			report.add_missing(doc)

		ELSE IF candidates.len() == 1:
			doc.filed_path = candidates[0].file_path
			doc.path_status = 'relocated'
			doc.path_verified_at = now()
			report.add_relocated(doc, old_path, new_path)

		ELSE:  # multiple matches
			doc.path_status = 'ambiguous'
			report.add_ambiguous(doc, candidates)
			# User must resolve manually

		db.update_filed_doc(doc)

	RETURN report
```

### Handling Edge Cases

| Scenario | Handling |
|---|---|
| File deleted entirely | Mark as `missing`; check archive backup exists |
| File duplicated (same hash, multiple locations) | Mark as `ambiguous`; prefer path closest to original |
| File modified after filing (different hash) | Won't match — flag for manual review |
| Hash not stored (legacy records) | Compute from archive backup PDF if available |
| Filesystem index stale | Re-run `index` before `relocate` |

---

## Integration with Current Filing Pipeline

### Immediate Change (in existing C# code)

Even before the Rust tool is built, the filing pipeline should start storing hashes. This is a small change to `ScanDocHandler.cs`:

**In `CopyAndMoveTheFile`**, after the file copy succeeds:

```csharp
// After: bResult = CopyFile(source, fdi.filedAs_pathAndFileName, ref _docFilingStatusStr);
// Add:
if (bResult)
{
	long fileLen;
	byte[] hash = GenHashOnFileExcludingMetadata(fdi.filedAs_pathAndFileName, out fileLen);
	fdi.fileHash = hash;
	fdi.fileSize = fileLen;
	fdi.hashAlgorithm = "md5-excl-metadata";  // current algorithm
}
```

This requires adding `fileHash`, `fileSize`, and `hashAlgorithm` fields to `FiledDocInfo`. Since MongoDB is schema-flexible, old records without these fields will simply have them as `null`.

**In `ProcessPdfFile`**, the archive copy hash could also be stored in `ScanDocInfo`:

```csharp
// After archive copy succeeds:
long archiveLen;
byte[] archiveHash = GenHashOnFileExcludingMetadata(archiveFileName, out archiveLen);
// Store in scanDocInfo for future reference
```

### Future Integration

When the Rust relocator is built:

1. The C# filing code calls `scan-relocator hash-file` (via `Process.Start`) after filing, or
2. The filing code writes the hash directly to the database (as above), and the Rust tool only handles indexing + relocation
3. Eventually the entire filing pipeline moves to Rust or a new stack

---

## Migration Path

### Phase 1: Store hashes at filing time (C# change, immediate)
- Add `fileHash` / `hashAlgorithm` fields to `FiledDocInfo`
- Compute and store hash in `CopyAndMoveTheFile`
- All newly filed documents now have hashes

### Phase 2: Build the Rust CLI tool
- Implement `index` command (replaces "Recompute MD5" button)
- Implement `check` command (reports missing files)
- Implement `relocate` command with `--dry-run`
- Target MongoDB first (current DB)

### Phase 3: Backfill legacy records
- For filed documents without hashes, compute from archive backup PDFs
- `scan-relocator backfill --db ... --archive-folder ...`

### Phase 4: Add Postgres support
- Implement `PostgresDocumentStore`
- Add migration tooling for MongoDB → Postgres

### Phase 5: Retire C# maintenance view
- Remove "Recompute MD5" button (replaced by CLI)
- Remove manual "Locate File" (replaced by automatic relocation)
- Keep the Audit View for reviewing relocation reports

---

## Performance Estimates

Assuming ~50,000 files across the filing folders:

| Operation | Current (C# + MD5 over SMB) | Proposed (Rust + BLAKE3 local) |
|---|---|---|
| Full index build | ~2-4 hours | ~5-15 minutes |
| Single file hash | ~100ms | ~1-5ms |
| Relocation check | N/A (manual) | ~2-5 seconds (DB queries only) |

Over Tailscale/remote SMB, the bottleneck is network I/O not hashing, so the Rust speed advantage is less pronounced for remote files. The indexer should ideally run on the file server itself or a machine on the local network.

---

## Backup Requirements

Before running any relocation:

1. **MongoDB dump**: `mongodump --db ScanManager --out backup_$(date +%Y%m%d)`
   - Critical collections: `FiledDocInfo`, `ScanDocInfo`, `ExistingFileInfo`
2. **The `--dry-run` flag** should always be used first
3. **The `--backup-json` flag** saves old paths before updating, enabling rollback
4. **Archive PDFs** (`ScanDocBackups`) are the ultimate safety net — if a file is truly lost, it can be re-filed from the archive
