# RadioHomeEngine

An HLS-based streaming audio server with SiriusXM, Lyrion Music Server, and audio CD integration.

RadioHomeEngine provides a variety of HLS streams which are generated essentially on demand and cached internally;
streams that are no longer actively being streamed are stopped until they have a new listener.

These streams can be played on compatible devices through Lyrion Music Server, or on a variety of other devices (including some game consoles, smart TVs, and virtually all smartphones) through the web UI.

## Features

### Integration with [Lyrion Music Server](https://lyrion.org) (`--lyrion`)

RadioHomeEngine maintains a connection to a Lyrion server through a Telnet connection on localhost port 9090.
Through this connection, RadioHomeEngine can:

* play one of its channels on a connected device (requires [PlayHLS](https://forums.lyrion.org/forum/user-forums/3rd-party-software/99692-announce-playhls-plugin-plugin-to-play-apple-hls-m3u8-stream))
* play a CD inserted in the host computer's CD drive on a connected device

Other Lyrion plugins can be used to allow streaming to Google Cast and AirPlay devices.

#### Infrared remote control

RadioHomeEngine can intercept commands from infrared remotes and perform specific actions:
    * simulate button presses on a "real" remote
    * simulate button presses on the device itself
    * display a numeric entry prompt and play the corresponding channel
    * show information about what's playing on the current channel, or on another channel number that you've entered
    * rewind ten seconds

The file `LyrionIR.fs` contains infrared remote control mappings for an RCA RPJ166 projector remote.

### SiriusXM (`--siriusxm`)

RadioHomeEngine can use the credentials defined in `username.txt` and `password.txt` to establish a connection with the SiriusXM web app.
All live SiriusXM channels (which generally occupy the range 1-999) are added to RadioHomeEngine's channel list.
RadioHomeEngine will start a new session for each individual channel number, allowing simultaneous playback of separate channels.

### Audio CD integration (`--cd`)

RadioHomeEngine's CD integration uses `udevadm` to monitor connected CD drives on Linux.
Both audio CDs (leveraging `icedax` and `abcde`) and data CDs are supported.
(Mixed-mode CDs are treated as audio CDs; this can be changed with a small edit to `DiscDriveStatus.fs`).

Track information for audio CDs can be pulled from MusicBrainz or from CD-Text.

With one or more CDs inserted, RadioHomeEngine can:

* Play the CD on a connected device (via Lyrion)
* Rip the CD
* Generate an HLS stream that plays all tracks (channel number 2000)
* Generate an HLS stream that shuffles all tracks (channel number 2020)

Streams are also generated for each individual CD drive (channel numbers 2001+ and 2021+).

While RadioHomeEngine is streaming data from one CD drive, you can remove and replace CDs in other drives without interrupting the stream.

Note that when you rip a CD, `abcde` handles metadata retrieval itself (to support album art, etc).
You might want to add a [`.abcde.conf`](https://manpages.debian.org/trixie/abcde/abcde.1.en.html) configuration file.
Some example options you might use include:

    CDDBMETHOD=cdtext,musicbrainz
    WAVOUTPUTDIR=/tmp
    LOWDISK=y

### Brown noise

Channel 3010 hosts a continuous brown noise generator.

### Weather forecasts (`--weather` and `--weather-alerts`)

RadioHomeEngine can provide forecasts from `weather.gov`, using `espeak` for text-to-speech.
Your latitude and longitude will be read from `location.txt`.

With the flag `--weather`, forecasts will be provided on channel 3020.

With the flag `--weather-alerts`, RadioHomeEngine will check for new alerts every five minutes, and temporarily override the contents of any active channel with the contents of the alert.

### ALSA input

On Linux systems with ALSA, RadioHomeEngine will enumerate audio input devices and generate channels for each of them, in the 2100+ range.

### Shuffle by folder name (`artist-shuffle`)

RadioHomeEngine can generate a channel by shuffling a set of music files.
An example of this is in `StreamRepository.fs`, where a set of hardcoded artist names is used to create channels 1001+.

### Local radio and television (`--local-radio`)

RadioHomeEngine can pull in online audio streams and generate channels for them.
Some HLS streams can be used directly, but other streams might need to be re-encoded via `ShellSegmentSourceFactory` and the `RemoteStream` union case.

An example of this is in `StreamRepository.fs`, where a handful of radio stations and a few TV streams are added.
(RadioHomeEngine does not support video; output streams are audio-only.)

## Build

* Install Visual Studio 2026 Community
* Open the `.sln` file
* Right-click on the project `RadioHomeEngine.AspNetCore` and click Publish

If you're running LMS on an ordinary PC, you'll probably want to create a single-file, self-contained build for Linux-x64.
This executable can then be moved to the Linux computer, marked as executable, and run, perhaps from a starup script through `screen`.
