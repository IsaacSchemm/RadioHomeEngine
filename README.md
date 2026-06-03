# Radio Home Engine

An ASP.NET Core sidecar for Lyrion Music Server.

RadioHomeEngine is designed to run on a Linux server that is also running [Lyrion Music Server](https://lyrion.org/) (LMS).

Functionality available from the web interface or API (port 5000):

* Audio CD playback
    * Album and track names (from CD-Text or MusicBrainz)
    * Play an audio or data CD on a connected device
    * Rip an audio or data CD to LMS's media folder
* SiriusXM
    * List channels (live streams only; Xtra channels not supported)
    * Play a channel on a connected device
    * Play a channel in the browser, and view a list of recently played songs on the channel
* Weather
    * Play weather forecast (API only)

Functionality available from a custom infrared remote:

* Stop (0 + "OK")
* Play a CD (00 + "OK")
* View the artist and title of a CD (00 + "Alternate Action")
* Play weather forecast (000 + "OK")
* Play a SiriusXM channel (Number + "OK")
* See what's playing on a SiriusXM channel (Number + "Alternate Action")

*This application's port 5000 should not be exposed to the internet* - like LMS, it is designed only for use within a local home network.

## Build

* Install Visual Studio 2026 Community
* Open the `.sln` file
* Right-click on the project `RadioHomeEngine.AspNetCore` and click Publish

If you're running LMS on an ordinary PC, you'll probably want to create a
single-file, self-contained build for Linux-x64. (Radio Home Engine does not
currently support LMS on Windows.) This executable can then be moved to the
Linux computer, marked as executable, and run, perhaps from `/etc/rc.local`
and/or through `screen`.

## Configuration

The user's latitude and longitude (used for weather alerts and forecasts) are stored in `location.txt`.
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

In the default mapping, the "Source" button is used to flip between modes for the number buttons, and the number buttons trigger a prompt that can activate "atomic actions".
See the file `AtomicActions.fs` for these codes.

### Audio CD playback

RadioHomeEngine supports more than one CD drive.

When playing a CD using infrared remote commands, all inserted CDs will be added to the playlist, in order.

Note that when you rip a CD, `abcde` handles metadata retrieval itself (to support album art, etc).
You might want to add a [`.abcde.conf`](https://manpages.debian.org/trixie/abcde/abcde.1.en.html) configuration file.
Some example options you might use include:

    CDDBMETHOD=cdtext,musicbrainz
    WAVOUTPUTDIR=/tmp
    LOWDISK=y
