import jp.virtualcd.player.LanguageStrings;
public final class LanguageStringsTest {
    public static void main(String[] args){
        check("ja".equals(LanguageStrings.code()),"Japanese is the default");
        check("設定".equals(LanguageStrings.text("設定","Settings")),"Japanese label");
        LanguageStrings.setCode("en");
        check("Settings".equals(LanguageStrings.text("設定","Settings")),"English label");
        LanguageStrings.setCode("ja");
        check("設定".equals(LanguageStrings.text("設定","Settings")),"Switch back without process restart");
        LanguageStrings.setCode("invalid");
        check("ja".equals(LanguageStrings.code()),"Unknown languages fall back to Japanese");
        System.out.println("LanguageStringsTest PASS");
    }
    private static void check(boolean result,String label){if(!result)throw new AssertionError(label);}
}
