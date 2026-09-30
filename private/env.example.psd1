# Settings the build scripts read, kept out of git. Copy this file to
# private\env.psd1 and fill in only what you use; an empty value sets nothing.
# A variable already set in the environment wins over this file. The file is
# read as data, never run. docs/developer/private-files.md explains each one.
@{
    # The Android signing key, when it is kept outside private\.
    # Default: private\android.keystore, then %USERPROFILE%\.wslc-agent\android.keystore.
    WSLC_AGENT_KEYSTORE         = ''
    WSLC_AGENT_KEYSTORE_PASS    = ''
    # Default: wslc-agent.
    WSLC_AGENT_KEY_ALIAS        = ''
    # Default: the keystore's password.
    WSLC_AGENT_KEY_PASS         = ''

    # The Android client's Firebase configuration, when it is kept outside private\.
    # Default: private\google-services.json.
    WSLC_AGENT_GOOGLE_SERVICES  = ''

    # The Firebase service account key the agent installer carries.
    # Default: private\firebase-service-account.json.
    WSLC_AGENT_PUSH_KEY         = ''
}
