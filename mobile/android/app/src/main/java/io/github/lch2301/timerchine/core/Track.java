package io.github.lch2301.timerchine.core;

public final class Track {
    public String uri = "";
    public String title = "";
    public boolean remote;
    public String thumbnailPath = "";

    public Track() { }

    public Track(String uri, String title, boolean remote) {
        this.uri = uri == null ? "" : uri;
        this.title = title == null ? "" : title;
        this.remote = remote;
    }

    public Track copy() {
        Track copy = new Track(uri, title, remote);
        copy.thumbnailPath = thumbnailPath;
        return copy;
    }
}
