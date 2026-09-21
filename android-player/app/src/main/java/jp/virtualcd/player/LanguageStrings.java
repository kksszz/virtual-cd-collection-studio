package jp.virtualcd.player;

/** App-owned labels only. Never translate track metadata, lyrics or filesystem names. */
public final class LanguageStrings {
    private LanguageStrings(){}
    private static volatile boolean english;
    public static String text(String japanese,String translated){return english?translated:japanese;}
    public static String code(){return english?"en":"ja";}
    public static void setCode(String code){english="en".equals(code);}
}
