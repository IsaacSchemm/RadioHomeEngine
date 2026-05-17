namespace RadioHomeEngine

module Discovery =
    let getDriveInfo scope = [
        for device in DiscDrives.getDevices scope do {
            device = device
            disc = {
                audio = DiscDriveStatus.tryGetAudioDiscInfo device
                data = DataCD.tryGetDataDiscInfo device
            }
        }
    ]
