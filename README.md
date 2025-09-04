# ScanMonitor

A comprehensive document management application that monitors for scanned PDF documents and provides semi-automated filing capabilities with OCR text extraction, document classification, and database storage.

## Overview

ScanMonitor is a Windows WPF application designed to automate the processing and filing of scanned documents. The application monitors specified folders for new PDF files, extracts text and images, stores document information in a MongoDB database, and provides tools for organizing and filing documents efficiently.

## Key Features

- **Automatic folder monitoring** for new PDF files
- **OCR text extraction** from scanned documents
- **Image thumbnail generation** for quick document preview
- **Document classification** using configurable rules and patterns
- **MongoDB database storage** for document metadata and content
- **Semi-automated filing** with customizable naming conventions
- **Email notifications** for follow-up actions and calendar events
- **Cross-platform file monitoring** with network folder support

## System Architecture

### Core Components

1. **ScanFileMonitor** - Main monitoring service that watches folders for new files
2. **ScanDocHandler** - Document processing engine that handles PDF analysis and database operations
3. **DocTypesMatcher** - Document classification system using pattern matching
4. **ScanFolderWatcher** - File system watcher for real-time folder monitoring

### Database Structure

The application uses MongoDB with the following collections:

- **ScanDocInfo** - Document metadata (unique name, page count, creation date, original filename)
- **ScanDocPages** - Extracted text content and page information
- **FiledDocInfo** - Filed document tracking with filing details and status
- **DocTypes** - Document classification rules and patterns
- **ExistingFileInfo** - File hash information for duplicate detection

## How It Works

### File Monitoring Process

1. **Folder Watching**: The application monitors configured folders (default: `c:\Users\Rob\Documents\ScanSnap`) for new PDF files using `FileSystemWatcher`

2. **File Detection**: When a PDF file is detected:
   - The system waits for the file to be fully written (accessibility check)
   - Generates a unique name based on timestamp and filename
   - Checks if the document already exists in the database

3. **Document Processing**: For new documents:
   - Creates an archive copy in the backup location
   - Extracts text content using PDF text extraction
   - Generates thumbnail images (150 DPI) for visual preview
   - Stores document metadata and content in MongoDB

4. **Background Monitoring**: A background thread continuously:
   - Scans watched folders every 10 seconds (configurable)
   - Processes any missed files not caught by file system events
   - Handles file modifications (e.g., A3 scanner adding pages)
   - Moves filed documents to the appropriate archive folder

### Data Storage

#### ScanDocInfo Collection
Stores core document metadata:
- `uniqName` - Unique identifier (timestamp_filename format)
- `numPages` - Total page count
- `numPagesWithText` - Pages containing extractable text
- `createDate` - Document creation timestamp
- `origFileName` - Original file path
- `flagForHelpFiling` - Manual review flag

#### ScanDocPages Collection
Contains extracted content:
- `uniqName` - Document identifier
- `scanPagesText` - Text elements with positional information
- `pageRotations` - Page orientation data

#### FiledDocInfo Collection
Tracks filing operations:
- Document type classification
- Filed location and filename
- Filing timestamp and status
- Follow-up and calendar information
- Email notification settings

### Document Classification

The `DocTypesMatcher` system uses configurable rules to automatically classify documents based on:
- Text content patterns
- Regular expressions
- Document structure analysis
- Historical filing patterns

### File Processing Pipeline

1. **Archive Creation** - Copy to backup location (`\\MACALLAN\Admin\ScanAdmin\ScanDocBackups`)
2. **Text Extraction** - OCR processing with positional information
3. **Image Generation** - Thumbnail creation for UI display
4. **Database Storage** - Metadata and content storage in MongoDB
5. **Classification** - Automatic document type detection
6. **Filing Queue** - Addition to unfiled documents list

## Configuration

### Database Settings
- **Connection String**: `mongodb://macallan/`
- **Database Name**: `ScanManager`
- **Collections**: ScanDocInfo, ScanDocPages, FiledDocInfo, DocTypes, ExistingFileInfo

### Folder Configuration
- **Monitor Folders**: Configurable list of folders to watch
- **Archive Folder**: `\\MACALLAN\Admin\ScanAdmin\ScanDocBackups`
- **Image Storage**: `\\MACALLAN\Admin\ScanAdmin\ScannedDocImgs`
- **Filed Documents**: `c:\Users\Rob\Documents\AlreadyFiledDocs`

### Processing Limits
- **Max Pages for Images**: 500 pages
- **Max Pages for Text**: 500 pages
- **Thumbnail Resolution**: 150 DPI
- **Monitor Interval**: 10 seconds

## Email Integration

The application can send email notifications for:
- **Follow-up reminders** for specific document types
- **Calendar appointments** with document attachments
- **Filing confirmations** and status updates

Email configuration includes:
- SMTP server settings (Gmail by default)
- Recipient lists with display names
- Calendar invitation generation (vCalendar format)

## Requirements

- **Windows** operating system
- **.NET Framework 4.8**
- **MongoDB** database server
- **GhostScript 9.26** (32-bit) for PDF processing

### Important GhostScript Note
This program requires GhostScript (32 bit) but doesn't work with versions greater than 9.26:
- Download: https://github.com/ArtifexSoftware/ghostpdl-downloads/releases/tag/gs926
- Reference: https://stackoverflow.com/questions/56205425/ghostscriptrasterizer-objects-returns-0-as-pagecount-value

## User Interface

The application provides several views:
- **Main Monitor** - Status display and folder monitoring information
- **Filing View** - Document review and filing interface
- **Audit Trail** - Filed document history and statistics
- **Maintenance** - System configuration and database management
- **Settings** - Application preferences and folder configuration
