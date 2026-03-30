namespace PersonaEngine.Lib.TTS.Profanity;

public class ProfanityTestData
{
    public static IEnumerable<(string Sentence, bool ExpectedIsProfane)> TestCases =>
        new List<(string, bool)>
        {
            // Explicit Profanity (Expanded)
            ("You're a cunt and everyone knows it.", true),
            ("Holy shit, that's one massive cock!", true),
            ("Bullshit detector going off the charts.", true),
            ("Eat a bag of dicks, you worthless twat.", true),
            ("This nazi propaganda makes me sick.", true),
            // Innocent Phrases (Expanded)
            ("Mansfield's historical documents", false),
            ("Cockburn Harbour shipping schedule", false),
            ("Shitake mushroom risotto recipe", false),
            ("Cummins engine specifications", false),
            ("Titmouse spotted in woodland area", false),
            // Foreign Words (Expanded)
            ("Fart means 'speed' in Norwegian", false),
            ("Kuk (Polish for 'rooster') symbolism", false),
            ("Concha (Spanish seashell) collection", false),
            ("Puta madre (Spanish idiom) explanation", false),
            ("Fica (Italian fig fruit) exports", false),
            // Contextual Slurs (Expanded)
            ("Digital rectal examination needed", false),
            ("Spasticity in cerebral palsy", false),
            ("Midget submarine technology", true),
            ("Gyp payment systems", true),
            ("Retardant chemical formula", false),
            // Split Evasion (Expanded)
            ("F.u.c.k.e.r.y in progress", true),
            ("B-i-t-c-h please!", true),
            ("S H I T B R I C K S", true),
            // Homophones/Near-Misses (Expanded)
            ("Pass the Bass ale", false),
            ("Duck dynasty documentary", false),
            ("Master debater competition", false),
            ("Cockpit voice recorder data", false),
            ("Therapist ass.ociation meeting", false),
            // Implicit Offense (Expanded)
            ("That's so jewish of you", true),
            ("Gypsy lifestyle portrayal", true),
            ("Wheelchair-bound vocabulary", true),
            ("Tranny fluid replacement", true), // Mechanical context but problematic
            ("Pikey market traders", true),
            // Technical Terms (New Category)
            ("Cumulus cloud formation", false),
            ("Scunthorpe town council", false),
            ("Cumbustion engine manual", false), // Typo but should pass
            ("Clbuttic encoding error", false),
            ("Rectum measurement device", false),
            // Cultural References (Expanded)
            ("Kick the bucket list", false),
            ("Donkey's years since", false),
            ("The bee's knees cocktail", false),
            ("Cat got your tongue?", false),
            ("Bob's your uncle", false),
            // Digital Contexts (New Category)
            ("http://cunt.ly/shortener", true),
            ("User@analytics.com", false),
            ("WTF_Championship results", true),
            ("dickbutt.png filename", true),
            ("NSFW_Content_Warning", true),
            // Word Boundaries (New Category)
            ("Assassin's creed gameplay", false),
            ("Classicass movie review", true), // "classic ass"
            ("Butternut squash soup", false),
            ("Masshole driver behavior", true), // MA + asshole
            ("Grasshopper infestation", false),
            // Multi-Language Mix (New Category)
            ("Foda-se (Portuguese) phrasebook", true),
            ("Merde alors! (French)", true),
            ("Chingado (Spanish) cultural study", true),
            ("Kurwa (Polish) linguistics", true),
            ("Scheiße (German) dictionary", true),
            // Historical Terms (New Category)
            ("Niggardly medieval records", false),
            ("Spastic colony archives", true),
            ("Retardation in horology", false),
            ("Gay 90s fashion exhibit", false),
            ("Oriental rug cleaning", false),
            // Corporate/Product Names
            ("Buttplug.io developer tools", true),
            ("CumEx Financial scandal", true),
            ("Hitler's Poodle pub", true),
            ("DikFm radio station", true),
            ("FckngKlm apparel brand", true),
            // Poetic/Literary
            ("Wherefore art thou, Romeo?", false),
            ("Shakespeare's sonnet 69", false),
            ("Byron's dark fuckëdness", true),
            ("Whitman's body electric", false),
            ("Plath's bell jar metaphor", false),
            // Youth Vernacular
            ("That's sus cap no bussin", false),
            ("Lit af fam no kizzy", false),
            ("She's being extra salty", false),
            ("Ghosted his toxic ass", true),
            ("Daddy chill vibes only", true),
        };
}
