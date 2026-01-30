# Radio Home Engine

An ASP.NET Core sidecar for Lyrion Music Server.

RadioHomeEngine is designed to run on a Linux server that is also running [Lyrion Music Server](https://lyrion.org/) (LMS).

Functionality available from the web interface (port 5000):

* Audio CD playback
    * Album and track names (from CD-Text or MusicBrainz)
    * Play a disc (all tracks, or all audio files) on a device connected to LMS
    * Rip the disc to LMS's media folder using `abcde`
* SiriusXM
    * List channels (live streams only; Xtra channels not supported)
    * Play a channel on a device connected to LMS
    * Play a channel in the browser, and view a list of recently played songs

Functionality available from a custom infrared remote:

* Audio CD playback
    * Album and track names (from CD-Text or MusicBrainz)
    * Play a disc (all tracks, or all audio files)
    * Rip the disc to LMS's media folder using `abcde`
* SiriusXM
    * Play channel by number (live streams only; Xtra channels not supported)
    * View currently playing program title (for the last SiriusXM channel number entered)
* U.S. weather forcasts and alerts from `weather.gov`, using `espeak` speech synthesis

*This application's port 5000 should not be exposed to the internet* - like LMS, it is designed only for use within a local home network.

## Build

* Install Visual Studio 2022 Community
* Open the `.sln` file
* Right-click on the project `RadioHomeEngine.AspNetCore` and click Publish

If you're running LMS on an ordinary PC, you'll probably want to create a
single-file, self-contained build for Linux-x64. (Radio Home Engine does not
currently support LMS on Windows.) This executable can then be moved to the
Linux computer, marked as executable, and run, perhaps from `/etc/rc.local`
and/or through `screen`.

## Configuration

The user's latitude and longitude (used for weather forecasts) are stored in `location.txt`.
Their SiriusXM username and password are stored in `username.txt` and `password.txt`, respectively.

(In both cases, the software assumes the user is in the U.S.)

## Other dependencies

* `ip`
* `ffmpeg`
* `udevadm`
* `eject`
* `icedax`
* `abcde`
* `espeak`

## Features

### IR

RadioHomeEngine monitors LMS for infrared remote commands, and performs actions based on the mappings in `LyrionIR.fs`.

In the default mapping, the "Source" button is used to flip between modes for the number buttons, and the number buttons trigger a prompt that can activate "atomic actions":

* 00: Information
* 01: Play CD
* 02: Rip CD
* 03: Eject CD
* 09: Weather

### Audio CD playback

RadioHomeEngine supports more than one CD drive.

When playing a CD using infrared remote commands, all inserted CDs will be added to the playlist, in order.

Note that when you rip a CD, `abcde` handles metadata retrieval itself (to support album art, etc).
You might want to add a [`.abcde.conf`](https://manpages.debian.org/trixie/abcde/abcde.1.en.html) configuration file.
Some example options you might use include:

    CDDBMETHOD=cdtext,musicbrainz
    WAVOUTPUTDIR=/tmp
    LOWDISK=y
